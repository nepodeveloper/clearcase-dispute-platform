using System.Security.Cryptography;
using System.Text.Json;
using ClearCase.Application.DisputeEligibility;
using ClearCase.Application.Transactions;
using ClearCase.Domain.DisputeEligibility;
using ClearCase.Domain.Disputes;

namespace ClearCase.Application.Disputes;

public sealed record SubmitDisputeCommand(
    Guid TransactionId, string ReasonCode, string Description, string? IdempotencyKey = null)
{
    /// <summary>SHA-256 of the canonical normalized payload; detects key reuse with a different request.</summary>
    public string Fingerprint() => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new[] { TransactionId.ToString("D"), ReasonCode, Description })));
}

public abstract record SubmitDisputeResult
{
    public sealed record Created(DisputeView Dispute) : SubmitDisputeResult;
    public sealed record Replayed(DisputeView Dispute) : SubmitDisputeResult;
    public sealed record IdempotencyKeyReused : SubmitDisputeResult;
    public sealed record TransactionNotFound : SubmitDisputeResult;
    public sealed record InvalidReason : SubmitDisputeResult;
    public sealed record Ineligible(DisputeEligibilityResult Eligibility) : SubmitDisputeResult;
    public sealed record ActiveDisputeExists : SubmitDisputeResult;
}

public interface ISubmitDisputeHandler
{
    Task<SubmitDisputeResult> HandleAsync(SubmitDisputeCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Opens a dispute case after re-evaluating eligibility. It records intent only and never
/// initiates a chargeback, reversal or refund.
/// </summary>
public sealed class SubmitDisputeHandler(
    ICustomerContext customerContext,
    ITransactionReader transactionReader,
    DisputeEligibilityEvaluator evaluator,
    IDisputeStore disputeStore) : ISubmitDisputeHandler
{
    public async Task<SubmitDisputeResult> HandleAsync(
        SubmitDisputeCommand command, CancellationToken cancellationToken)
    {
        var customerId = customerContext.CustomerId;
        var submission = command.IdempotencyKey is { } key
            ? new DisputeSubmission(key, command.Fingerprint())
            : null;
        // A replay must return the original outcome even if eligibility or case state has since changed.
        if (await ResolveSubmissionAsync(customerId, submission, cancellationToken) is { } prior)
            return prior;

        var transaction = await transactionReader.ReadForEligibilityAsync(
            customerId, command.TransactionId, cancellationToken);
        if (transaction is null)
            return new SubmitDisputeResult.TransactionNotFound();
        if (!DisputeReasonCatalog.IsValid(transaction.Category, command.ReasonCode))
            return new SubmitDisputeResult.InvalidReason();

        var eligibility = await evaluator.EvaluateAsync(customerId, transaction, cancellationToken);
        if (eligibility.Outcome == DisputeEligibilityOutcome.Ineligible)
            return new SubmitDisputeResult.Ineligible(eligibility);
        if (await disputeStore.HasOpenDisputeAsync(customerId, transaction.Id, cancellationToken))
            return await ConflictAsync(customerId, submission, cancellationToken);

        var dispute = Dispute.Submit(
            Guid.NewGuid(), NewReference(), customerId, transaction,
            command.ReasonCode, command.Description, eligibility, Guid.NewGuid());
        return await disputeStore.AddAsync(dispute, submission, cancellationToken) == DisputeAddResult.Added
            ? new SubmitDisputeResult.Created(DisputeView.From(dispute))
            : await ConflictAsync(customerId, submission, cancellationToken);
    }

    // A concurrent request with the same key may have committed first; prefer its outcome over a generic conflict.
    private async Task<SubmitDisputeResult> ConflictAsync(
        Guid customerId, DisputeSubmission? submission, CancellationToken cancellationToken) =>
        await ResolveSubmissionAsync(customerId, submission, cancellationToken)
            ?? new SubmitDisputeResult.ActiveDisputeExists();

    private async Task<SubmitDisputeResult?> ResolveSubmissionAsync(
        Guid customerId, DisputeSubmission? submission, CancellationToken cancellationToken)
    {
        if (submission is null)
            return null;
        var stored = await disputeStore.FindSubmissionAsync(customerId, submission.IdempotencyKey, cancellationToken);
        if (stored is null)
            return null;
        if (!string.Equals(stored.RequestFingerprint, submission.RequestFingerprint, StringComparison.Ordinal))
            return new SubmitDisputeResult.IdempotencyKeyReused();
        var dispute = await disputeStore.ReadAsync(customerId, stored.DisputeId, cancellationToken)
            ?? throw new InvalidOperationException("An idempotency record references a missing dispute.");
        return new SubmitDisputeResult.Replayed(dispute);
    }

    private static string NewReference() =>
        "DSP-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
}

public interface IGetDisputeHandler
{
    Task<DisputeView?> HandleAsync(Guid disputeId, CancellationToken cancellationToken);
}

public sealed class GetDisputeHandler(ICustomerContext customerContext, IDisputeStore disputeStore)
    : IGetDisputeHandler
{
    public Task<DisputeView?> HandleAsync(Guid disputeId, CancellationToken cancellationToken) =>
        disputeStore.ReadAsync(customerContext.CustomerId, disputeId, cancellationToken);
}
