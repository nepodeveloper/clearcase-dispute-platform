using ClearCase.Api.Authentication;
using ClearCase.Api.Disputes;
using ClearCase.Api.Endpoints;
using ClearCase.Api.Middleware;
using ClearCase.Api.Transactions;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Disputes;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClearCase.Api.Features.Disputes.WithdrawDispute;

public sealed record WithdrawDisputeRequest(long? ExpectedVersion, string? Reason);

public static class WithdrawDisputeEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/disputes/{id}/withdrawals", Withdraw)
            .WithName("WithdrawDispute")
            .WithSummary("Withdraw an undecided dispute")
            .WithDescription("Allowed only while the decision is Pending, no payment adjustment has been requested, "
                + "there is no active supervisor escalation and (card purchases) no chargeback exists. Requires the "
                + "current expectedVersion. The case becomes Closed with a CustomerWithdrawn timeline event; no money "
                + "moves. The customer may open a new dispute for the transaction afterwards. A reason of at most "
                + "500 characters is optional. Returns 409 dispute.withdrawalNotAllowed or dispute.concurrencyConflict.")
            .Accepts<WithdrawDisputeRequest>("application/json")
            .Produces<DisputeResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthenticationPolicies.CustomerAccess)
            .RequireCsrfProtection();

    private static async Task<Results<Ok<DisputeResponse>, ProblemHttpResult>> Withdraw(
        string id, WithdrawDisputeRequest? request, HttpContext context, IWithdrawDisputeHandler handler,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(id, out var disputeId))
            return ApiProblem.Create(context, StatusCodes.Status400BadRequest, "Invalid dispute ID",
                "dispute.invalidId", errors: [new("/id", "disputeId.invalid", "Enter a valid dispute ID.")]);

        var errors = new List<QueryError>();
        if (request?.ExpectedVersion is null or <= 0)
            errors.Add(new("/expectedVersion", "expectedVersion.invalid", "Send the current positive case version."));
        var reason = request?.Reason?.Trim();
        if (reason?.Length > DisputeWithdrawalRules.ReasonMaxLength)
            errors.Add(new("/reason", "reason.tooLong",
                $"The reason must be at most {DisputeWithdrawalRules.ReasonMaxLength} characters."));
        if (errors.Count != 0)
            return ApiProblem.Create(context, StatusCodes.Status400BadRequest, "Invalid withdrawal request",
                "dispute.invalidRequest", errors: errors);

        var dispute = await handler.HandleAsync(new WithdrawDisputeCommand(
            disputeId, request!.ExpectedVersion!.Value, string.IsNullOrEmpty(reason) ? null : reason,
            (string)context.Items[CorrelationIdMiddleware.ContextItemKey]!), cancellationToken);
        return dispute is null
            ? ApiProblem.Create(context, StatusCodes.Status404NotFound, "Dispute not found", "dispute.notFound")
            : TypedResults.Ok(DisputeResponse.From(dispute));
    }
}
