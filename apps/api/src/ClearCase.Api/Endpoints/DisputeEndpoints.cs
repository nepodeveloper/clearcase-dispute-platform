using ClearCase.Api.Disputes;
using ClearCase.Api.Authentication;
using ClearCase.Api.Features.Disputes.SubmitDispute;
using ClearCase.Api.Transactions;
using ClearCase.Application.Disputes;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace ClearCase.Api.Endpoints;

public static class DisputeEndpoints
{
    public static WebApplication MapDisputeEndpoints(this WebApplication app)
    {
        foreach (var (prefix, suffix) in new[] { ("/api/v1", ""), ("/v1", "Alias") })
        {
            app.MapGet($"{prefix}/disputes", List)
                .WithName("ListDisputes" + suffix)
                .WithSummary("List the authenticated customer's submitted disputes")
                .WithDescription("Newest first, then reference ascending. pageSize defaults to 20 (1-100). "
                    + "Pass the opaque pageInfo.nextCursor unchanged for the next page. New submissions appear on refresh.")
                .Produces<DisputeListResponse>().ProducesProblem(400).ProducesProblem(401)
                .RequireAuthorization(AuthenticationPolicies.CustomerAccess);

            app.MapGet($"{prefix}/disputes/{{id}}/events", Events)
                .WithName("GetDisputeEvents" + suffix)
                .WithSummary("Get an owned case's customer-safe chronological timeline")
                .WithDescription("Returns an array in ascending sequenceNumber order; sequence is canonical even when timestamps tie. "
                    + "Never returns internal audit records or operation secrets.")
                .Produces<DisputeTimelineEventResponse[]>().ProducesProblem(400)
                .ProducesProblem(401).ProducesProblem(404)
                .RequireAuthorization(AuthenticationPolicies.CustomerAccess);

            SubmitDisputeEndpoint.Map(app, prefix, suffix);

            app.MapGet($"{prefix}/disputes/{{id}}", Get)
                .WithName("GetDispute" + suffix)
                .WithSummary("Get an owned dispute and its timeline")
                .Produces<DisputeResponse>()
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .RequireAuthorization(AuthenticationPolicies.CustomerAccess);
        }

        return app;
    }

    private static async Task<Results<Ok<DisputeListResponse>, ProblemHttpResult>> List(
        [FromQuery] string? pageSize, [FromQuery] string? cursor, HttpContext context,
        IListDisputesHandler handler, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var size = 20;
        var errors = new List<QueryError>();
        if (pageSize is not null && (!int.TryParse(pageSize, NumberStyles.None, CultureInfo.InvariantCulture, out size)
            || size is < 1 or > 100))
            errors.Add(new("/pageSize", "pageSize.invalid", "Choose a page size from 1 to 100."));
        DisputeCursor? after = null;
        if (cursor is not null && !DisputeCursorCodec.TryDecode(cursor, out after))
            errors.Add(new("/cursor", "cursor.invalid", "Use a valid continuation cursor returned by this endpoint."));
        if (errors.Count != 0)
            return ApiProblem.Create(context, 400, "Invalid dispute query", "dispute.invalidQuery", errors: errors);
        var page = await handler.HandleAsync(new ListDisputesQuery(size, after), cancellationToken);
        return TypedResults.Ok(new DisputeListResponse(page.Items.Select(DisputeSummaryResponse.From).ToArray(),
            new DisputePageInfo(page.HasNextPage, page.HasNextPage ? DisputeCursorCodec.Encode(page.Items[^1]) : null)));
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
