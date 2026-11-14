using ClearCase.Api.Disputes;
using ClearCase.Api.Authentication;
using ClearCase.Api.Middleware;
using ClearCase.Api.Transactions;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Disputes;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace ClearCase.Api.Endpoints;

public static class DisputeEndpoints
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string IdempotentReplayedHeader = "Idempotent-Replayed";
    private const int IdempotencyKeyMaxLength = 128;

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

            app.MapPost($"{prefix}/disputes", Create)
                .WithName("CreateDispute" + suffix)
                .WithSummary("Submit a dispute for an owned transaction")
                .WithDescription("Re-evaluates eligibility and opens a case. It does not reverse, refund or charge back funds. "
                    + "Send a unique Idempotency-Key (1-128 visible ASCII characters) per submission: retrying with the same key "
                    + "and payload returns the original case with Idempotent-Replayed: true; the same key with a different "
                    + "payload returns 409 dispute.idempotencyKeyReused.")
                .Accepts<CreateDisputeRequest>("application/json")
                .Produces<DisputeResponse>(StatusCodes.Status201Created)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
                .RequireAuthorization(AuthenticationPolicies.CustomerAccess)
                .RequireCsrfProtection();

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
            return Problem(context, 400, "Invalid dispute query", "dispute.invalidQuery", errors: errors);
        var page = await handler.HandleAsync(new ListDisputesQuery(size, after), cancellationToken);
        return TypedResults.Ok(new DisputeListResponse(page.Items.Select(DisputeSummaryResponse.From).ToArray(),
            new DisputePageInfo(page.HasNextPage, page.HasNextPage ? DisputeCursorCodec.Encode(page.Items[^1]) : null)));
    }

    private static async Task<Results<Ok<DisputeTimelineEventResponse[]>, ProblemHttpResult>> Events(
        string id, HttpContext context, IGetDisputeHandler handler, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(id, out var disputeId))
            return Problem(context, 400, "Invalid dispute ID", "dispute.invalidId",
                errors: [new("/id", "disputeId.invalid", "Enter a valid dispute ID.")]);
        var dispute = await handler.HandleAsync(disputeId, cancellationToken);
        return dispute is null ? Problem(context, 404, "Dispute not found", "dispute.notFound")
            : TypedResults.Ok(dispute.Timeline.Select(DisputeTimelineEventResponse.From).ToArray());
    }

    private static async Task<Results<Created<DisputeResponse>, ProblemHttpResult>> Create(
        CreateDisputeRequest request, [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        HttpContext context, ISubmitDisputeHandler handler, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var errors = new List<QueryError>();
        if (context.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var keyValues)
            && (keyValues.Count != 1 || !IsValidIdempotencyKey(keyValues[0])))
            errors.Add(new("/headers/" + IdempotencyKeyHeader, "idempotencyKey.invalid",
                $"Send one Idempotency-Key of 1 to {IdempotencyKeyMaxLength} visible ASCII characters."));
        if (!Guid.TryParse(request.TransactionId, out var transactionId))
            errors.Add(new("/transactionId", "transactionId.invalid", "Enter a valid transaction ID."));
        var reasonCode = request.ReasonCode?.Trim() ?? string.Empty;
        if (reasonCode.Length == 0)
            errors.Add(new("/reasonCode", "reasonCode.required", "Select a dispute reason."));
        var description = request.Description?.Trim() ?? string.Empty;
        if (description.Length is < Dispute.DescriptionMinLength or > Dispute.DescriptionMaxLength)
            errors.Add(new("/description", "description.length",
                $"Describe the problem in {Dispute.DescriptionMinLength} to {Dispute.DescriptionMaxLength} characters."));
        if (errors.Count != 0)
            return Problem(context, StatusCodes.Status400BadRequest, "Invalid dispute request",
                "dispute.invalidRequest", errors: errors);

        var result = await handler.HandleAsync(
            new SubmitDisputeCommand(transactionId, reasonCode, description, idempotencyKey), cancellationToken);
        if (result is SubmitDisputeResult.Replayed)
            context.Response.Headers[IdempotentReplayedHeader] = "true";
        return result switch
        {
            SubmitDisputeResult.Created created => TypedResults.Created(
                $"{context.Request.Path.Value!.TrimEnd('/')}/{created.Dispute.Id}",
                DisputeResponse.From(created.Dispute)),
            SubmitDisputeResult.Replayed replayed => TypedResults.Created(
                $"{context.Request.Path.Value!.TrimEnd('/')}/{replayed.Dispute.Id}",
                DisputeResponse.From(replayed.Dispute)),
            SubmitDisputeResult.IdempotencyKeyReused => Problem(context,
                StatusCodes.Status409Conflict, "Idempotency key already used", "dispute.idempotencyKeyReused",
                "This Idempotency-Key was already used for a different dispute request. Use a new key for a new request."),
            SubmitDisputeResult.TransactionNotFound => Problem(context,
                StatusCodes.Status422UnprocessableEntity, "Transaction not found", "dispute.transactionNotFound",
                errors: [new("/transactionId", "transactionId.notFound", "The transaction could not be found.")]),
            SubmitDisputeResult.InvalidReason => Problem(context,
                StatusCodes.Status422UnprocessableEntity, "Invalid dispute reason", "dispute.invalidReason",
                errors: [new("/reasonCode", "reasonCode.invalid", "Select a reason that applies to this transaction.")]),
            SubmitDisputeResult.Ineligible ineligible => Problem(context,
                StatusCodes.Status422UnprocessableEntity, "Transaction is not eligible for dispute",
                "dispute.transactionIneligible", ineligible.Eligibility.Explanation,
                extensions: new() { ["reasonCodes"] = ineligible.Eligibility.ReasonCodes }),
            SubmitDisputeResult.ActiveDisputeExists => Problem(context,
                StatusCodes.Status409Conflict, "An open dispute already exists", "dispute.activeCaseExists",
                "This transaction already has an open dispute."),
            _ => throw new InvalidOperationException("Unhandled dispute submission result.")
        };
    }

    private static async Task<Results<Ok<DisputeResponse>, ProblemHttpResult>> Get(
        string id, HttpContext context, IGetDisputeHandler handler, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(id, out var disputeId))
            return Problem(context, StatusCodes.Status400BadRequest, "Invalid dispute ID", "dispute.invalidId",
                errors: [new("/id", "disputeId.invalid", "Enter a valid dispute ID.")]);

        var dispute = await handler.HandleAsync(disputeId, cancellationToken);
        return dispute is null
            ? Problem(context, StatusCodes.Status404NotFound, "Dispute not found", "dispute.notFound")
            : TypedResults.Ok(DisputeResponse.From(dispute));
    }

    private static bool IsValidIdempotencyKey(string? key) =>
        key is { Length: > 0 and <= IdempotencyKeyMaxLength } && key.All(character => character is > ' ' and <= '~');

    internal static ProblemHttpResult Problem(
        HttpContext context, int status, string title, string code, string? detail = null,
        IReadOnlyList<QueryError>? errors = null, Dictionary<string, object?>? extensions = null)
    {
        extensions ??= [];
        extensions["code"] = code;
        extensions["correlationId"] = context.Items[CorrelationIdMiddleware.ContextItemKey];
        if (errors is not null)
            extensions["errors"] = errors;
        return TypedResults.Problem(statusCode: status, title: title, detail: detail, extensions: extensions);
    }
}
