using System.Globalization;
using System.Security.Claims;
using System.Text;
using ClearCase.Api.Authentication;
using ClearCase.Api.Disputes;
using ClearCase.Api.Middleware;
using ClearCase.Application.AgentDisputes;
using ClearCase.Api.Transactions;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace ClearCase.Api.Endpoints;

public static class AgentDisputeEndpoints
{
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 100;
    private const int MaximumSearchLength = 100;
    private const int IdempotencyKeyMaxLength = 128;
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    public static WebApplication MapAgentDisputeEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/agent/disputes", List)
            .WithName("ListAgentDisputes")
            .WithSummary("List persisted disputes within the synthetic bank-wide Agent scope")
            .WithDescription("Searches case reference, transaction description, or reason code. "
                + "Filters by category and caseStatus. Ordered newest first, then reference ascending. "
                + "The server returns at most 100 records; pass pageInfo.nextCursor unchanged to continue. "
                + "Cases are visible to authorized bank employees; no case assignment is implied.")
            .Produces<AgentDisputeListResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(AuthenticationPolicies.BankEmployeeAccess);

        app.MapGet("/api/v1/agent/disputes/{id}", Get)
            .WithName("GetAgentDispute")
            .WithSummary("Get an operational dispute investigation view")
            .Produces<AgentDisputeDetailResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthenticationPolicies.BankEmployeeAccess);

        app.MapGet("/api/v1/agent/disputes/{id}/notes", ListNotes)
            .WithName("ListInvestigationNotes")
            .WithSummary("List private append-only investigation notes")
            .Produces<InvestigationNoteResponse[]>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthenticationPolicies.BankEmployeeAccess);

        app.MapPost("/api/v1/agent/disputes/{id}/notes", CreateNote)
            .WithName("CreateInvestigationNote")
            .WithSummary("Append a private investigation note")
            .WithDescription("Requires an Idempotency-Key. The same key and normalized text replay the existing note; "
                + "reusing the key with different text returns 409. Notes are private to authorized bank employees.")
            .Accepts<CreateInvestigationNoteRequest>("application/json")
            .Produces<InvestigationNoteResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthenticationPolicies.BankEmployeeAccess)
            .RequireCsrfProtection();

        return app;
    }

    private static async Task<Results<Ok<AgentDisputeListResponse>, ProblemHttpResult>> List(
        [FromQuery] string? pageSize,
        [FromQuery] string? cursor,
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] string? caseStatus,
        HttpContext context,
        IListAgentDisputesHandler handler,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var errors = new List<QueryError>();
        var size = DefaultPageSize;
        if (pageSize is not null && (!int.TryParse(pageSize, NumberStyles.None,
                CultureInfo.InvariantCulture, out size) || size is < 1 or > MaximumPageSize))
        {
            errors.Add(new("/pageSize", "pageSize.invalid", $"Choose a page size from 1 to {MaximumPageSize}."));
        }

        AgentDisputeCursor? after = null;
        if (cursor is not null && !TryDecodeCursor(cursor, out after))
            errors.Add(new("/cursor", "cursor.invalid", "Use a valid continuation cursor returned by this endpoint."));

        var querySearch = search?.Trim();
        if (querySearch?.Length > MaximumSearchLength)
            errors.Add(new("/search", "search.tooLong", $"Search must be at most {MaximumSearchLength} characters."));

        TransactionCategory? parsedCategory = null;
        if (category is not null)
        {
            if (!Enum.TryParse(category, ignoreCase: false, out TransactionCategory parsed)
                || !Enum.IsDefined(parsed) || parsed.ToString() != category)
                errors.Add(new("/category", "category.invalid", "Choose CardPurchase, DebitOrder, or BankCharge."));
            else
                parsedCategory = parsed;
        }

        DisputeCaseStatus? parsedStatus = null;
        if (caseStatus is not null)
        {
            if (!Enum.TryParse(caseStatus, ignoreCase: false, out DisputeCaseStatus parsed)
                || !Enum.IsDefined(parsed) || parsed.ToString() != caseStatus)
                errors.Add(new("/caseStatus", "caseStatus.invalid", "Choose a supported case status."));
            else
                parsedStatus = parsed;
        }

        if (errors.Count != 0)
            return ApiProblem.Create(context, StatusCodes.Status400BadRequest,
                "Invalid agent dispute query", "agentDispute.invalidQuery", errors: errors);

        var page = await handler.HandleAsync(
            new ListAgentDisputesQuery(size, string.IsNullOrEmpty(querySearch) ? null : querySearch,
                parsedCategory, parsedStatus, after), cancellationToken);
        var nextCursor = page.HasNextPage ? EncodeCursor(page.Items[^1]) : null;
        return TypedResults.Ok(new AgentDisputeListResponse(
            page.Items.Select(AgentDisputeListItemResponse.From).ToArray(),
            new AgentPageInfo(page.HasNextPage, nextCursor)));
    }

    private static async Task<Results<Ok<AgentDisputeDetailResponse>, ProblemHttpResult>> Get(
        string id,
        HttpContext context,
        IGetAgentDisputeHandler handler,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(id, out var disputeId))
            return InvalidId(context);

        var dispute = await handler.HandleAsync(disputeId, cancellationToken);
        return dispute is null
            ? DisputeNotFound(context)
            : TypedResults.Ok(AgentDisputeDetailResponse.From(
                dispute, SimulationAuthorization.IsEnabled(environment)));
    }

    private static async Task<Results<Ok<InvestigationNoteResponse[]>, ProblemHttpResult>> ListNotes(
        string id,
        HttpContext context,
        IGetInvestigationNotesHandler handler,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(id, out var disputeId))
            return InvalidId(context);
        var notes = await handler.HandleAsync(disputeId, cancellationToken);
        return notes is null
            ? DisputeNotFound(context)
            : TypedResults.Ok(notes.Select(InvestigationNoteResponse.From).ToArray());
    }

    private static async Task<Results<Created<InvestigationNoteResponse>, ProblemHttpResult>> CreateNote(
        string id,
        CreateInvestigationNoteRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        HttpContext context,
        ICreateInvestigationNoteHandler handler,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(id, out var disputeId))
            return InvalidId(context);

        var errors = new List<QueryError>();
        var idempotencyValues = context.Request.Headers[IdempotencyKeyHeader];
        if (idempotencyValues.Count != 1 || !IsValidIdempotencyKey(idempotencyKey))
        {
            errors.Add(new("/headers/Idempotency-Key", "idempotencyKey.invalid",
                $"Send one Idempotency-Key of 1 to {IdempotencyKeyMaxLength} visible ASCII characters."));
        }

        string normalizedText;
        var validUnicode = true;
        try
        {
            normalizedText = request.Text?.Trim().Normalize(NormalizationForm.FormC) ?? string.Empty;
        }
        catch (ArgumentException)
        {
            normalizedText = string.Empty;
            validUnicode = false;
            errors.Add(new("/text", "agentNote.invalidUnicode", "Note text must contain valid Unicode characters."));
        }
        if (validUnicode && normalizedText.Length is < CreateInvestigationNoteHandler.MinimumLength
            or > CreateInvestigationNoteHandler.MaximumLength)
        {
            errors.Add(new("/text", "agentNote.length",
                $"Note text must contain {CreateInvestigationNoteHandler.MinimumLength} to "
                + $"{CreateInvestigationNoteHandler.MaximumLength} normalized characters."));
        }
        if (errors.Count > 0)
            return ApiProblem.Create(context, StatusCodes.Status400BadRequest,
                "Invalid investigation note", "agentNote.invalidRequest", errors: errors);

        var employeeIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(employeeIdValue, out var employeeId))
            return ApiProblem.Create(context, StatusCodes.Status403Forbidden,
                "Bank employee identity is unavailable", "auth.employeeIdentityRequired");

        var correlationId = (string)context.Items[CorrelationIdMiddleware.ContextItemKey]!;
        var result = await handler.HandleAsync(new CreateInvestigationNoteCommand(
            disputeId, employeeId, normalizedText, idempotencyKey!, correlationId), cancellationToken);

        return result.Outcome switch
        {
            InvestigationNoteWriteOutcome.Created => CreatedNote(context, result.Note!, replayed: false),
            InvestigationNoteWriteOutcome.Replayed => CreatedNote(context, result.Note!, replayed: true),
            InvestigationNoteWriteOutcome.IdempotencyKeyReused => ApiProblem.Create(context,
                StatusCodes.Status409Conflict, "Idempotency key already used", "agentNote.idempotencyKeyReused",
                "This Idempotency-Key was already used for different note text."),
            InvestigationNoteWriteOutcome.DisputeNotFound => DisputeNotFound(context),
            _ => throw new InvalidOperationException("Unhandled investigation note result.")
        };
    }

    private static Created<InvestigationNoteResponse> CreatedNote(
        HttpContext context, InvestigationNote note, bool replayed)
    {
        if (replayed)
            context.Response.Headers["Idempotent-Replayed"] = "true";
        return TypedResults.Created(context.Request.Path.Value!, InvestigationNoteResponse.From(note));
    }

    private static ProblemHttpResult InvalidId(HttpContext context) =>
        ApiProblem.Create(context, StatusCodes.Status400BadRequest, "Invalid dispute ID",
            "agentDispute.invalidId",
            errors: [new("/id", "disputeId.invalid", "Enter a valid dispute ID.")]);

    private static ProblemHttpResult DisputeNotFound(HttpContext context) =>
        ApiProblem.Create(context, StatusCodes.Status404NotFound, "Dispute not found",
            "agentDispute.notFound");

    private static bool IsValidIdempotencyKey(string? key) =>
        key is { Length: > 0 and <= IdempotencyKeyMaxLength }
        && key.All(character => character is > ' ' and <= '~');

    private static string EncodeCursor(AgentDisputeListItem item) =>
        Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(
            $"1|{item.CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}|{item.Reference}"));

    private static bool TryDecodeCursor(string value, out AgentDisputeCursor? cursor)
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

            cursor = new AgentDisputeCursor(new DateTimeOffset(ticks, TimeSpan.Zero), parts[2]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
