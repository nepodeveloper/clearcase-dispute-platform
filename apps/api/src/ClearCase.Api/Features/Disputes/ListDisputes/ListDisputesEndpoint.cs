using System.Globalization;
using ClearCase.Api.Authentication;
using ClearCase.Api.Disputes;
using ClearCase.Api.Endpoints;
using ClearCase.Api.Transactions;
using ClearCase.Application.Disputes;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace ClearCase.Api.Features.Disputes.ListDisputes;

public static class ListDisputesEndpoint
{
    public static void Map(IEndpointRouteBuilder app, string prefix, string suffix)
    {
        app.MapGet($"{prefix}/disputes", List)
                .WithName("ListDisputes" + suffix)
                .WithSummary("List the authenticated customer's submitted disputes")
                .WithDescription("Newest first, then reference ascending. pageSize defaults to 20 (1-100). "
                    + "Pass the opaque pageInfo.nextCursor unchanged for the next page. New submissions appear on refresh.")
                .Produces<DisputeListResponse>().ProducesProblem(400).ProducesProblem(401)
                .RequireAuthorization(AuthenticationPolicies.CustomerAccess);

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
}
