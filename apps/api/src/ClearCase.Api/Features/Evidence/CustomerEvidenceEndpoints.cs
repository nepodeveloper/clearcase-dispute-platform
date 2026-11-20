using System.Text.Json.Serialization;
using ClearCase.Api.Authentication;
using ClearCase.Api.Endpoints;
using ClearCase.Api.Middleware;
using ClearCase.Api.Transactions;
using ClearCase.Application.Evidence;
using ClearCase.Domain.Evidence;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClearCase.Api.Features.Evidence;

/// <summary>Customer-supplied dispute evidence; distinct from merchant representment evidence.</summary>
public sealed record EvidenceResponse(
    Guid Id, string FileName, string ContentType, long SizeBytes, DateTimeOffset UploadedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note)
{
    public static EvidenceResponse From(CustomerEvidenceSummary item) =>
        new(item.Id, item.FileName, item.ContentType, item.SizeBytes, item.UploadedAt, item.Note);
}

public sealed record EvidenceListResponse(IReadOnlyList<EvidenceResponse> Items);

/// <summary>Documents the multipart body for OpenAPI; the endpoint reads the form directly.</summary>
public sealed record EvidenceUploadForm(IFormFile File, string? Note);

public static class CustomerEvidenceEndpoints
{
    private const string FileField = "file";
    private const string NoteField = "note";
    private const int MaxRequestBodyBytes = CustomerEvidenceRules.MaxFileBytes + 512 * 1024;

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/disputes/{id}/evidence", Upload)
            .WithName("UploadCustomerEvidence")
            .WithSummary("Upload a supporting file for an owned dispute")
            .WithDescription("multipart/form-data with a file field and an optional note (at most 200 characters). "
                + "PDF, PNG or JPEG up to 5 MB, verified by file signature rather than the declared content type; "
                + "at most 5 files per dispute. The file is malware-scanned before storage; with no scanner configured "
                + "the upload fails closed with 503 evidence.scanUnavailable. Evidence cannot be deleted.")
            .Accepts<EvidenceUploadForm>("multipart/form-data")
            .Produces<EvidenceResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireAuthorization(AuthenticationPolicies.CustomerAccess)
            .RequireCsrfProtection();

        app.MapGet("/api/v1/disputes/{id}/evidence", List)
            .WithName("ListCustomerEvidence")
            .WithSummary("List evidence uploaded for an owned dispute, oldest first")
            .Produces<EvidenceListResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthenticationPolicies.CustomerAccess);

        app.MapGet("/api/v1/disputes/{id}/evidence/{evidenceId}/content", Download)
            .WithName("DownloadCustomerEvidence")
            .WithSummary("Download an owned evidence file as an attachment")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthenticationPolicies.CustomerAccess);
    }

    private static async Task<Results<Created<EvidenceResponse>, ProblemHttpResult>> Upload(
        string id, HttpContext context, IUploadCustomerEvidenceHandler handler, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!EvidenceProblems.TryParseId(id, "dispute.invalidId", "disputeId", context, out var disputeId, out var invalid))
            return invalid!;

        var form = await ReadFormAsync(context, cancellationToken);
        var file = form?.Files.GetFile(FileField);
        byte[]? content = null;
        if (file is { Length: > 0 })
        {
            // One byte over the limit is enough for the handler to reject an oversized file without buffering all of it.
            content = new byte[Math.Min(file.Length, CustomerEvidenceRules.MaxFileBytes + 1)];
            await using var stream = file.OpenReadStream();
            await stream.ReadExactlyAsync(content, cancellationToken);
        }

        var result = await handler.HandleAsync(new UploadCustomerEvidenceCommand(
            disputeId, file?.FileName, content, form?[NoteField].ToString()), cancellationToken);
        return result switch
        {
            UploadCustomerEvidenceResult.Created created => TypedResults.Created(
                $"/api/v1/disputes/{disputeId}/evidence/{created.Evidence.Id}/content",
                EvidenceResponse.From(created.Evidence)),
            UploadCustomerEvidenceResult.InvalidFile invalidFile => ApiProblem.Create(context,
                StatusCodes.Status400BadRequest, "Invalid evidence file", "evidence.invalidFile",
                errors: [new(invalidFile.Pointer, invalidFile.Code, invalidFile.Message)]),
            UploadCustomerEvidenceResult.DisputeClosed => ApiProblem.Create(context,
                StatusCodes.Status409Conflict, "Dispute is closed", "dispute.closed",
                "Evidence cannot be added to a closed dispute."),
            UploadCustomerEvidenceResult.LimitReached => ApiProblem.Create(context,
                StatusCodes.Status409Conflict, "Evidence limit reached", "evidence.limitReached",
                $"A dispute can hold at most {CustomerEvidenceRules.MaxFilesPerDispute} files."),
            UploadCustomerEvidenceResult.RejectedByScan => ApiProblem.Create(context,
                StatusCodes.Status422UnprocessableEntity, "File rejected", "evidence.rejectedByScan",
                "The file did not pass the security scan and was not stored."),
            UploadCustomerEvidenceResult.ScanUnavailable => ApiProblem.Create(context,
                StatusCodes.Status503ServiceUnavailable, "Security scan unavailable", "evidence.scanUnavailable",
                "Files cannot be accepted right now because the security scan is unavailable. Nothing was stored."),
            UploadCustomerEvidenceResult.DisputeNotFound => EvidenceProblems.DisputeNotFound(context),
            _ => throw new InvalidOperationException("Unhandled evidence upload result.")
        };
    }

    private static async Task<IFormCollection?> ReadFormAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.HasFormContentType)
            return null;
        var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false })
            limit.MaxRequestBodySize = MaxRequestBodyBytes;
        try
        {
            return await context.Request.ReadFormAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or BadHttpRequestException)
        {
            // A malformed or over-limit body is the caller's invalid file, not a server fault.
            return null;
        }
    }

    private static async Task<Results<Ok<EvidenceListResponse>, ProblemHttpResult>> List(
        string id, HttpContext context, CustomerEvidenceQueryHandler handler, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!EvidenceProblems.TryParseId(id, "dispute.invalidId", "disputeId", context, out var disputeId, out var invalid))
            return invalid!;
        var items = await handler.ListAsync(disputeId, cancellationToken);
        return items is null
            ? EvidenceProblems.DisputeNotFound(context)
            : TypedResults.Ok(new EvidenceListResponse(items.Select(EvidenceResponse.From).ToArray()));
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> Download(
        string id, string evidenceId, HttpContext context, CustomerEvidenceQueryHandler handler,
        CancellationToken cancellationToken)
    {
        if (!EvidenceProblems.TryParseId(id, "dispute.invalidId", "disputeId", context, out var disputeId, out var invalid)
            || !EvidenceProblems.TryParseId(evidenceId, "evidence.invalidId", "evidenceId", context, out var fileId, out invalid))
            return invalid!;
        return EvidenceProblems.AsAttachment(context, await handler.GetContentAsync(disputeId, fileId, cancellationToken));
    }
}

public static class StaffEvidenceEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/agent/disputes/{id}/evidence", List)
            .WithName("ListAgentCustomerEvidence")
            .WithSummary("List customer evidence for any dispute (read-only, bank-wide)")
            .Produces<EvidenceListResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthenticationPolicies.BankEmployeeAccess);

        app.MapGet("/api/v1/agent/disputes/{id}/evidence/{evidenceId}/content", Download)
            .WithName("DownloadAgentCustomerEvidence")
            .WithSummary("Download customer evidence as an attachment (read-only, bank-wide)")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthenticationPolicies.BankEmployeeAccess);
    }

    private static async Task<Results<Ok<EvidenceListResponse>, ProblemHttpResult>> List(
        string id, HttpContext context, StaffEvidenceQueryHandler handler, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!EvidenceProblems.TryParseId(id, "dispute.invalidId", "disputeId", context, out var disputeId, out var invalid))
            return invalid!;
        var items = await handler.ListAsync(disputeId, cancellationToken);
        return items is null
            ? EvidenceProblems.DisputeNotFound(context)
            : TypedResults.Ok(new EvidenceListResponse(items.Select(EvidenceResponse.From).ToArray()));
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> Download(
        string id, string evidenceId, HttpContext context, StaffEvidenceQueryHandler handler,
        CancellationToken cancellationToken)
    {
        if (!EvidenceProblems.TryParseId(id, "dispute.invalidId", "disputeId", context, out var disputeId, out var invalid)
            || !EvidenceProblems.TryParseId(evidenceId, "evidence.invalidId", "evidenceId", context, out var fileId, out invalid))
            return invalid!;
        return EvidenceProblems.AsAttachment(context, await handler.GetContentAsync(disputeId, fileId, cancellationToken));
    }
}

internal static class EvidenceProblems
{
    public static bool TryParseId(
        string value, string code, string name, HttpContext context, out Guid id, out ProblemHttpResult? problem)
    {
        problem = null;
        if (Guid.TryParse(value, out id))
            return true;
        problem = ApiProblem.Create(context, StatusCodes.Status400BadRequest, "Invalid identifier", code,
            errors: [new($"/{name}", $"{name}.invalid", "Enter a valid identifier.")]);
        return false;
    }

    public static ProblemHttpResult DisputeNotFound(HttpContext context) =>
        ApiProblem.Create(context, StatusCodes.Status404NotFound, "Dispute not found", "dispute.notFound");

    public static Results<FileContentHttpResult, ProblemHttpResult> AsAttachment(
        HttpContext context, CustomerEvidenceContent? content)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (content is null)
            return ApiProblem.Create(context, StatusCodes.Status404NotFound, "Evidence not found", "evidence.notFound");
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.File(content.Content, content.ContentType, content.FileName);
    }
}
