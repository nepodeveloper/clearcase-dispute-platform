using System.Security.Cryptography;
using ClearCase.Application.Disputes;
using ClearCase.Application.Transactions;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Evidence;

namespace ClearCase.Application.Evidence;

public sealed record CustomerEvidenceSummary(
    Guid Id, string FileName, string ContentType, long SizeBytes, DateTimeOffset UploadedAt, string? Note);

public sealed record CustomerEvidenceContent(string FileName, string ContentType, byte[] Content);

public sealed record NewCustomerEvidence(
    Guid Id, Guid DisputeId, string FileName, string ContentType, byte[] Content, string Sha256,
    string? Note, DateTimeOffset UploadedAt);

public enum CustomerEvidenceAddOutcome { Added, DisputeNotFound, DisputeClosed, LimitReached }

/// <summary>Evidence persistence; a null customer scope is the bank-wide read used by staff.</summary>
public interface ICustomerEvidenceStore
{
    Task<CustomerEvidenceAddOutcome> AddAsync(
        Guid customerId, NewCustomerEvidence evidence, CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerEvidenceSummary>?> ListAsync(
        Guid? customerId, Guid disputeId, CancellationToken cancellationToken);

    Task<CustomerEvidenceContent?> ReadContentAsync(
        Guid? customerId, Guid disputeId, Guid evidenceId, CancellationToken cancellationToken);
}

public enum MalwareScanVerdict { Clean, Infected, Unavailable }

/// <summary>Uploads fail closed: nothing is stored unless a configured scanner reports Clean.</summary>
public interface IMalwareScanner
{
    Task<MalwareScanVerdict> ScanAsync(ReadOnlyMemory<byte> content, CancellationToken cancellationToken);
}

public sealed record UploadCustomerEvidenceCommand(
    Guid DisputeId, string? FileName, byte[]? Content, string? Note);

public abstract record UploadCustomerEvidenceResult
{
    public sealed record Created(CustomerEvidenceSummary Evidence) : UploadCustomerEvidenceResult;
    public sealed record DisputeNotFound : UploadCustomerEvidenceResult;
    public sealed record DisputeClosed : UploadCustomerEvidenceResult;
    public sealed record InvalidFile(string Pointer, string Code, string Message) : UploadCustomerEvidenceResult;
    public sealed record LimitReached : UploadCustomerEvidenceResult;
    public sealed record RejectedByScan : UploadCustomerEvidenceResult;
    public sealed record ScanUnavailable : UploadCustomerEvidenceResult;
}

public interface IUploadCustomerEvidenceHandler
{
    Task<UploadCustomerEvidenceResult> HandleAsync(
        UploadCustomerEvidenceCommand command, CancellationToken cancellationToken);
}

public sealed class UploadCustomerEvidenceHandler(
    ICustomerContext customerContext,
    IDisputeStore disputes,
    ICustomerEvidenceStore store,
    IMalwareScanner scanner,
    TimeProvider clock) : IUploadCustomerEvidenceHandler
{
    public async Task<UploadCustomerEvidenceResult> HandleAsync(
        UploadCustomerEvidenceCommand command, CancellationToken cancellationToken)
    {
        var customerId = customerContext.CustomerId;
        // Ownership is established before any file detail is judged, so another customer's case is never confirmed.
        var dispute = await disputes.ReadAsync(customerId, command.DisputeId, cancellationToken);
        if (dispute is null)
            return new UploadCustomerEvidenceResult.DisputeNotFound();
        if (dispute.CaseStatus == DisputeCaseStatus.Closed)
            return new UploadCustomerEvidenceResult.DisputeClosed();

        var note = command.Note?.Trim();
        if (note?.Length > CustomerEvidenceRules.NoteMaxLength)
            return new UploadCustomerEvidenceResult.InvalidFile("/note", "note.tooLong",
                $"The note must be at most {CustomerEvidenceRules.NoteMaxLength} characters.");
        if (command.Content is not { Length: > 0 } content)
            return Invalid("The file is missing or empty.");
        if (content.Length > CustomerEvidenceRules.MaxFileBytes)
            return Invalid("The file is larger than 5 MB.");
        if (CustomerEvidenceRules.Sniff(content) is not { } type)
            return Invalid("Upload a PDF, PNG or JPEG file.");

        switch (await scanner.ScanAsync(content, cancellationToken))
        {
            case MalwareScanVerdict.Infected: return new UploadCustomerEvidenceResult.RejectedByScan();
            case MalwareScanVerdict.Clean: break;
            default: return new UploadCustomerEvidenceResult.ScanUnavailable();
        }

        var evidence = new NewCustomerEvidence(
            Guid.NewGuid(), command.DisputeId, CustomerEvidenceRules.SanitizeFileName(command.FileName),
            type.ContentType, content, Convert.ToHexString(SHA256.HashData(content)),
            string.IsNullOrEmpty(note) ? null : note, clock.GetUtcNow());
        return await store.AddAsync(customerId, evidence, cancellationToken) switch
        {
            CustomerEvidenceAddOutcome.Added => new UploadCustomerEvidenceResult.Created(
                new CustomerEvidenceSummary(evidence.Id, evidence.FileName, evidence.ContentType,
                    evidence.Content.Length, evidence.UploadedAt, evidence.Note)),
            CustomerEvidenceAddOutcome.DisputeClosed => new UploadCustomerEvidenceResult.DisputeClosed(),
            CustomerEvidenceAddOutcome.LimitReached => new UploadCustomerEvidenceResult.LimitReached(),
            _ => new UploadCustomerEvidenceResult.DisputeNotFound()
        };
    }

    private static UploadCustomerEvidenceResult.InvalidFile Invalid(string message) =>
        new("/file", "file.invalid", message);
}

public sealed class CustomerEvidenceQueryHandler(ICustomerContext customerContext, ICustomerEvidenceStore store)
{
    public Task<IReadOnlyList<CustomerEvidenceSummary>?> ListAsync(
        Guid disputeId, CancellationToken cancellationToken) =>
        store.ListAsync(customerContext.CustomerId, disputeId, cancellationToken);

    public Task<CustomerEvidenceContent?> GetContentAsync(
        Guid disputeId, Guid evidenceId, CancellationToken cancellationToken) =>
        store.ReadContentAsync(customerContext.CustomerId, disputeId, evidenceId, cancellationToken);
}

public sealed class StaffEvidenceQueryHandler(ICustomerEvidenceStore store)
{
    public Task<IReadOnlyList<CustomerEvidenceSummary>?> ListAsync(
        Guid disputeId, CancellationToken cancellationToken) =>
        store.ListAsync(null, disputeId, cancellationToken);

    public Task<CustomerEvidenceContent?> GetContentAsync(
        Guid disputeId, Guid evidenceId, CancellationToken cancellationToken) =>
        store.ReadContentAsync(null, disputeId, evidenceId, cancellationToken);
}
