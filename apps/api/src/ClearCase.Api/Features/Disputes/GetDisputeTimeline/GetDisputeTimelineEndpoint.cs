using ClearCase.Api.Authentication;
using ClearCase.Api.Disputes;
using ClearCase.Api.Endpoints;
using ClearCase.Api.Transactions;
using ClearCase.Application.Disputes;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClearCase.Api.Features.Disputes.GetDisputeTimeline;

public static class GetDisputeTimelineEndpoint
{
    public static void Map(IEndpointRouteBuilder app, string prefix, string suffix)
    {
        app.MapGet($"{prefix}/disputes/{{id}}/events", Events)
                .WithName("GetDisputeEvents" + suffix)
                .WithSummary("Get an owned case's customer-safe chronological timeline")
                .WithDescription("Returns an array in ascending sequenceNumber order; sequence is canonical even when timestamps tie. "
                    + "Never returns internal audit records or operation secrets.")
                .Produces<DisputeTimelineEventResponse[]>().ProducesProblem(400)
                .ProducesProblem(401).ProducesProblem(404)
                .RequireAuthorization(AuthenticationPolicies.CustomerAccess);

    }

    private static async Task<Results<Ok<DisputeTimelineEventResponse[]>, ProblemHttpResult>> Events(
        string id, HttpContext context, IGetDisputeHandler handler, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(id, out var disputeId))
            return ApiProblem.Create(context, 400, "Invalid dispute ID", "dispute.invalidId",
                errors: [new("/id", "disputeId.invalid", "Enter a valid dispute ID.")]);
        var dispute = await handler.HandleAsync(disputeId, cancellationToken);
        return dispute is null ? ApiProblem.Create(context, 404, "Dispute not found", "dispute.notFound")
            : TypedResults.Ok(dispute.Timeline.Select(DisputeTimelineEventResponse.From).ToArray());
    }
}
