using ClearCase.Api.Authentication;
using ClearCase.Api.Disputes;
using ClearCase.Api.Endpoints;
using ClearCase.Api.Transactions;
using ClearCase.Application.Disputes;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClearCase.Api.Features.Disputes.GetDispute;

public static class GetDisputeEndpoint
{
    public static void Map(IEndpointRouteBuilder app, string prefix, string suffix)
    {
        app.MapGet($"{prefix}/disputes/{{id}}", Get)
                .WithName("GetDispute" + suffix)
                .WithSummary("Get an owned dispute and its timeline")
                .Produces<DisputeResponse>()
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .RequireAuthorization(AuthenticationPolicies.CustomerAccess);    }

    private static async Task<Results<Ok<DisputeResponse>, ProblemHttpResult>> Get(
        string id, HttpContext context, IGetDisputeHandler handler, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(id, out var disputeId))
            return ApiProblem.Create(context, StatusCodes.Status400BadRequest, "Invalid dispute ID", "dispute.invalidId",
                errors: [new("/id", "disputeId.invalid", "Enter a valid dispute ID.")]);

        var dispute = await handler.HandleAsync(disputeId, cancellationToken);
        return dispute is null
            ? ApiProblem.Create(context, StatusCodes.Status404NotFound, "Dispute not found", "dispute.notFound")
            : TypedResults.Ok(DisputeResponse.From(dispute));
    }
}
