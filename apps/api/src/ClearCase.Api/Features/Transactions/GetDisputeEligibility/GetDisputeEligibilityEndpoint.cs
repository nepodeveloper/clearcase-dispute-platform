using ClearCase.Api.Authentication;
using ClearCase.Api.Endpoints;
using ClearCase.Api.Transactions;
using ClearCase.Application.DisputeEligibility;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClearCase.Api.Features.Transactions.GetDisputeEligibility;

public static class GetDisputeEligibilityEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/transactions/{id}/dispute-eligibility", GetDisputeEligibility)
            .WithName("GetTransactionDisputeEligibility")
            .WithSummary("Evaluate dispute eligibility for an owned transaction")
            .WithDescription("Returns a read-only eligibility policy decision. Debit-order decisions may distinguish ordinary-reversal restrictions from investigation intake. It does not submit a dispute or initiate a financial operation.")
            .Produces<TransactionDisputeEligibilityResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .RequireAuthorization(AuthenticationPolicies.CustomerAccess);

        app.MapGet("/v1/transactions/{id}/dispute-eligibility", GetDisputeEligibility)
            .WithName("GetTransactionDisputeEligibilityAlias")
            .WithSummary("Compatibility alias for transaction dispute eligibility")
            .Produces<TransactionDisputeEligibilityResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .RequireAuthorization(AuthenticationPolicies.CustomerAccess);
    }

    private static async Task<Results<Ok<TransactionDisputeEligibilityResponse>, ProblemHttpResult>>
        GetDisputeEligibility(
            string id,
            HttpContext context,
            IGetDisputeEligibilityHandler handler,
            CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            return Task.CompletedTask;
        });
        if (!Guid.TryParse(id, out var transactionId))
        {
            return ApiProblem.Create(context, StatusCodes.Status400BadRequest,
                "Invalid transaction ID", "transaction.invalidId",
                errors: [new QueryError("/id", "transactionId.invalid", "Enter a valid transaction ID.")]);
        }

        var result = await handler.HandleAsync(transactionId, cancellationToken);
        if (result is null)
        {
            return ApiProblem.Create(context, StatusCodes.Status404NotFound,
                "Transaction not found", "transaction.notFound");
        }

        return TypedResults.Ok(TransactionDisputeEligibilityResponse.From(transactionId, result));
    }
}
