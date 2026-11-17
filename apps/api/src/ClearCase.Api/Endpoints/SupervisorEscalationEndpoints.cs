using System.Globalization;
using System.Security.Claims;
using System.Text;
using ClearCase.Api.Authentication;
using ClearCase.Api.Disputes;
using ClearCase.Api.Middleware;
using ClearCase.Api.Transactions;
using ClearCase.Application.SupervisorEscalations;
using ClearCase.Domain.Disputes;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace ClearCase.Api.Endpoints;

public static class SupervisorEscalationEndpoints
{
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 100;
    private const int MaximumSearchLength = 100;
    private const int MaximumNoteLength = 2000;
    private const int MinimumNoteLength = 15;
    private const int IdempotencyKeyMaxLength = 128;
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    public static WebApplication MapSupervisorEscalationEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/agent/disputes/{id}/escalations", Create)
            .WithName("CreateDisputeEscalation")
            .WithSummary("Escalate a dispute to an independent supervisor")
            .WithDescription("Stores a private agent note and publishes only safe customer progress. "
                + "Requires an Idempotency-Key; repeated matching requests replay the original escalation.")
            .Accepts<CreateDisputeEscalationRequest>("application/json")
            .Produces<AgentEscalationCreationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthenticationPolicies.AgentAccess)
            .RequireCsrfProtection();

        app.MapGet("/api/v1/supervisor/escalations", List)
            .WithName("ListSupervisorEscalations")
            .WithSummary("List escalations for supervisor review")
            .WithDescription("Searches dispute and escalation references. Results use stable cursor pagination.")
            .Produces<SupervisorEscalationListResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(AuthenticationPolicies.SupervisorAccess);

        app.MapGet("/api/v1/supervisor/escalations/{id}", Get)
            .WithName("GetSupervisorEscalation")
            .WithSummary("Get supervisor-only escalation details and audit history")
            .Produces<SupervisorEscalationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthenticationPolicies.SupervisorAccess);

        app.MapPost("/api/v1/supervisor/escalations/{id}/acknowledgements", Acknowledge)
            .WithName("AcknowledgeSupervisorEscalation")
            .WithSummary("Acknowledge an escalation before resolving it")
            .Produces<SupervisorEscalationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthenticationPolicies.SupervisorAccess)
            .RequireCsrfProtection();

        app.MapPost("/api/v1/supervisor/escalations/{id}/resolutions", Resolve)
            .WithName("ResolveSupervisorEscalation")
            .WithSummary("Resolve an acknowledged escalation")
            .Accepts<ResolveDisputeEscalationRequest>("application/json")
            .Produces<SupervisorEscalationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthenticationPolicies.SupervisorAccess)
            .RequireCsrfProtection();

        return app;
    }

    private static async Task<Results<Ok<SupervisorEscalationListResponse>, ProblemHttpResult>> List(
        [FromQuery] string? pageSize,
        [FromQuery] string? cursor,
        [FromQuery] string? search,
        [FromQuery] string? status,
        HttpContext context,
        ListSupervisorEscalationsHandler handler,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var errors = new List<QueryError>();
        var size = DefaultPageSize;
        if (pageSize is not null && (!int.TryParse(pageSize, NumberStyles.None,
                CultureInfo.InvariantCulture, out size) || size is < 1 or > MaximumPageSize))
            errors.Add(new("/pageSize", "pageSize.invalid", $"Choose a page size from 1 to {MaximumPageSize}."));

        EscalationCursor? after = null;
        if (cursor is not null && !TryDecodeCursor(cursor, out after))
            errors.Add(new("/cursor", "cursor.invalid", "Use a valid continuation cursor returned by this endpoint."));

        var querySearch = search?.Trim();
        if (querySearch?.Length > MaximumSearchLength)
            errors.Add(new("/search", "search.tooLong", $"Search must be at most {MaximumSearchLength} characters."));

        DisputeEscalationStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse(status, ignoreCase: false, out DisputeEscalationStatus parsed)
                || !Enum.IsDefined(parsed) || parsed.ToString() != status)
                errors.Add(new("/status", "status.invalid", "Choose Open, Acknowledged, or Resolved."));
            else
                parsedStatus = parsed;
        }

        if (errors.Count != 0)
            return DisputeEndpoints.Problem(context, StatusCodes.Status400BadRequest,
                "Invalid supervisor escalation query", "supervisorEscalation.invalidQuery", errors: errors);

        var page = await handler.HandleAsync(new ListSupervisorEscalationsQuery(
            size, string.IsNullOrEmpty(querySearch) ? null : querySearch, parsedStatus, after),
            cancellationToken);
        var nextCursor = page.HasNextPage ? EncodeCursor(page.Items[^1]) : null;
        return TypedResults.Ok(new SupervisorEscalationListResponse(
            page.Items.Select(SupervisorEscalationListItemResponse.From).ToArray(),
            new SupervisorEscalationPageInfo(page.HasNextPage, nextCursor)));
    }

    private static async Task<Results<Ok<SupervisorEscalationResponse>, ProblemHttpResult>> Get(
        string id,
        HttpContext context,
        GetSupervisorEscalationHandler handler,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(id, out var escalationId))
            return InvalidId(context);
        var escalation = await handler.HandleAsync(escalationId, cancellationToken);
        return escalation is null
            ? NotFound(context)
            : TypedResults.Ok(SupervisorEscalationResponse.From(escalation));
    }

    private static async Task<Results<Created<AgentEscalationCreationResponse>, ProblemHttpResult>> Create(
        string id,
        CreateDisputeEscalationRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        HttpContext context,
        CreateDisputeEscalationHandler handler,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(id, out var disputeId))
            return InvalidId(context);
        if (!TryValidateIdempotencyKey(context, idempotencyKey, out var keyError))
            return InvalidRequest(context, "Invalid escalation request", "supervisorEscalation.invalidRequest", [keyError!]);

        var errors = new List<QueryError>();
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (!DisputeEscalationRules.IsValidReason(reason))
            errors.Add(new("/reason", "reason.invalid", "Choose a supported escalation reason."));
        var note = NormalizeNote(request.Note, "/note", errors);
        if (errors.Count != 0)
            return InvalidRequest(context, "Invalid escalation request", "supervisorEscalation.invalidRequest", errors);

        if (!TryGetEmployeeId(context, out var employeeId))
            return EmployeeIdentityRequired(context);
        var correlationId = (string)context.Items[CorrelationIdMiddleware.ContextItemKey]!;
        var result = await handler.HandleAsync(new CreateDisputeEscalationCommand(
            disputeId, employeeId, reason, note, idempotencyKey!, correlationId), cancellationToken);

        return result.Outcome switch
        {
            EscalationWriteOutcome.Created => Created(context, disputeId, result.Escalation!, replayed: false),
            EscalationWriteOutcome.Replayed => Created(context, disputeId, result.Escalation!, replayed: true),
            _ => CreateError(context, result.Outcome)
        };
    }

    private static async Task<Results<Ok<SupervisorEscalationResponse>, ProblemHttpResult>> Acknowledge(
        string id,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        HttpContext context,
        AcknowledgeEscalationHandler handler,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(id, out var escalationId))
            return InvalidId(context);
        if (!TryValidateIdempotencyKey(context, idempotencyKey, out var keyError))
            return InvalidRequest(context, "Invalid acknowledgement request",
                "supervisorEscalation.invalidRequest", [keyError!]);
        if (!TryGetEmployeeId(context, out var employeeId))
            return EmployeeIdentityRequired(context);

        var correlationId = (string)context.Items[CorrelationIdMiddleware.ContextItemKey]!;
        var result = await handler.HandleAsync(new AcknowledgeEscalationCommand(
            escalationId, employeeId, idempotencyKey!, correlationId), cancellationToken);
        return ActionResult(context, result);
    }

    private static async Task<Results<Ok<SupervisorEscalationResponse>, ProblemHttpResult>> Resolve(
        string id,
        ResolveDisputeEscalationRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        HttpContext context,
        ResolveEscalationHandler handler,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(id, out var escalationId))
            return InvalidId(context);
        if (!TryValidateIdempotencyKey(context, idempotencyKey, out var keyError))
            return InvalidRequest(context, "Invalid resolution request",
                "supervisorEscalation.invalidRequest", [keyError!]);
        var errors = new List<QueryError>();
        var note = NormalizeNote(request.Note, "/note", errors);
        if (errors.Count != 0)
            return InvalidRequest(context, "Invalid resolution request",
                "supervisorEscalation.invalidRequest", errors);
        if (!TryGetEmployeeId(context, out var employeeId))
            return EmployeeIdentityRequired(context);

        var correlationId = (string)context.Items[CorrelationIdMiddleware.ContextItemKey]!;
        var result = await handler.HandleAsync(new ResolveEscalationCommand(
            escalationId, employeeId, note, idempotencyKey!, correlationId), cancellationToken);
        return ActionResult(context, result);
    }

    private static Results<Created<AgentEscalationCreationResponse>, ProblemHttpResult> Created(
        HttpContext context, Guid disputeId, SupervisorEscalationDetails escalation, bool replayed)
    {
        if (replayed)
            context.Response.Headers["Idempotent-Replayed"] = "true";
        return TypedResults.Created($"/api/v1/agent/disputes/{disputeId}",
            AgentEscalationCreationResponse.From(escalation));
    }

    private static Results<Ok<SupervisorEscalationResponse>, ProblemHttpResult> ActionResult(
        HttpContext context, EscalationWriteResult result)
    {
        if (result.Outcome == EscalationWriteOutcome.Replayed)
            context.Response.Headers["Idempotent-Replayed"] = "true";
        return result.Outcome is EscalationWriteOutcome.Applied or EscalationWriteOutcome.Replayed
            ? TypedResults.Ok(SupervisorEscalationResponse.From(result.Escalation!))
            : ActionError(context, result.Outcome);
    }

    private static ProblemHttpResult CreateError(HttpContext context, EscalationWriteOutcome outcome) =>
        outcome switch
        {
            EscalationWriteOutcome.IdempotencyKeyReused => Conflict(context,
                "Idempotency key already used", "supervisorEscalation.idempotencyKeyReused"),
            EscalationWriteOutcome.ActiveEscalationExists => Conflict(context,
                "An active escalation already exists", "supervisorEscalation.alreadyActive"),
            EscalationWriteOutcome.DisputeNotFound => DisputeNotFound(context),
            EscalationWriteOutcome.CaseNotEligible => Conflict(context,
                "Dispute cannot be escalated", "supervisorEscalation.caseNotEligible"),
            EscalationWriteOutcome.VersionConflict => Conflict(context,
                "Dispute version conflict", "supervisorEscalation.concurrencyConflict"),
            _ => throw new InvalidOperationException("Unhandled escalation creation outcome.")
        };

    private static Results<Ok<SupervisorEscalationResponse>, ProblemHttpResult> ActionError(
        HttpContext context, EscalationWriteOutcome outcome) =>
        outcome switch
        {
            EscalationWriteOutcome.IdempotencyKeyReused => Conflict(context,
                "Idempotency key already used", "supervisorEscalation.idempotencyKeyReused"),
            EscalationWriteOutcome.EscalationNotFound => NotFound(context),
            EscalationWriteOutcome.InvalidTransition => Conflict(context,
                "Invalid escalation transition", "supervisorEscalation.invalidTransition"),
            EscalationWriteOutcome.ReviewerMustBeIndependent => Conflict(context,
                "A different employee must review this escalation", "supervisorEscalation.independentReviewerRequired"),
            EscalationWriteOutcome.VersionConflict => Conflict(context,
                "Escalation version conflict", "supervisorEscalation.concurrencyConflict"),
            _ => throw new InvalidOperationException("Unhandled escalation action outcome.")
        };

    private static ProblemHttpResult InvalidId(HttpContext context) =>
        DisputeEndpoints.Problem(context, StatusCodes.Status400BadRequest, "Invalid escalation ID",
            "supervisorEscalation.invalidId",
            errors: [new("/id", "escalationId.invalid", "Enter a valid escalation ID.")]);

    private static ProblemHttpResult DisputeNotFound(HttpContext context) =>
        DisputeEndpoints.Problem(context, StatusCodes.Status404NotFound, "Dispute not found",
            "agentDispute.notFound");

    private static ProblemHttpResult NotFound(HttpContext context) =>
        DisputeEndpoints.Problem(context, StatusCodes.Status404NotFound, "Escalation not found",
            "supervisorEscalation.notFound");

    private static ProblemHttpResult Conflict(HttpContext context, string title, string code) =>
        DisputeEndpoints.Problem(context, StatusCodes.Status409Conflict, title, code);

    private static ProblemHttpResult EmployeeIdentityRequired(HttpContext context) =>
        DisputeEndpoints.Problem(context, StatusCodes.Status403Forbidden,
            "Bank employee identity is unavailable", "auth.employeeIdentityRequired");

    private static ProblemHttpResult InvalidRequest(
        HttpContext context, string title, string code, IReadOnlyList<QueryError> errors) =>
        DisputeEndpoints.Problem(context, StatusCodes.Status400BadRequest, title, code, errors: errors);

    private static string NormalizeNote(string? value, string pointer, List<QueryError> errors)
    {
        string note;
        try
        {
            note = value?.Trim().Normalize(NormalizationForm.FormC) ?? string.Empty;
        }
        catch (ArgumentException)
        {
            errors.Add(new(pointer, "note.invalidUnicode", "Note text must contain valid Unicode characters."));
            return string.Empty;
        }

        if (note.Length is < MinimumNoteLength or > MaximumNoteLength)
            errors.Add(new(pointer, "note.length",
                $"Note text must contain {MinimumNoteLength} to {MaximumNoteLength} normalized characters."));
        return note;
    }

    private static bool TryValidateIdempotencyKey(
        HttpContext context, string? value, out QueryError? error)
    {
        var values = context.Request.Headers[IdempotencyKeyHeader];
        var valid = values.Count == 1
            && value is { Length: > 0 and <= IdempotencyKeyMaxLength }
            && value.All(character => character is > ' ' and <= '~');
        error = valid ? null : new("/headers/Idempotency-Key", "idempotencyKey.invalid",
            $"Send one Idempotency-Key of 1 to {IdempotencyKeyMaxLength} visible ASCII characters.");
        return valid;
    }

    private static bool TryGetEmployeeId(HttpContext context, out Guid employeeId) =>
        Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out employeeId);

    private static string EncodeCursor(SupervisorEscalationItem item) =>
        Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(
            $"1|{item.CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}|{item.Reference}"));

    private static bool TryDecodeCursor(string value, out EscalationCursor? cursor)
    {
        cursor = null;
        if (value.Length is 0 or > 160)
            return false;
        try
        {
            var parts = Encoding.UTF8.GetString(
                Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(value)).Split('|');
            if (parts.Length != 3 || parts[0] != "1"
                || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks
                || parts[2].Length is 0 or > 64
                || !parts[2].All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
            {
                return false;
            }

            cursor = new EscalationCursor(new DateTimeOffset(ticks, TimeSpan.Zero), parts[2]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
