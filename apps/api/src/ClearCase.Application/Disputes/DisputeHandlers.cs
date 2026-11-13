using System.Security.Cryptography;
using ClearCase.Application.DisputeEligibility;
using ClearCase.Application.Transactions;
using ClearCase.Domain.DisputeEligibility;
using ClearCase.Domain.Disputes;

namespace ClearCase.Application.Disputes;

public sealed record SubmitDisputeCommand(Guid TransactionId, string ReasonCode, string Description);

public abstract record SubmitDisputeResult
{
    public sealed record Created(DisputeView Dispute) : SubmitDisputeResult;
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
            return new SubmitDisputeResult.ActiveDisputeExists();

        var dispute = Dispute.Submit(
            Guid.NewGuid(), NewReference(), customerId, transaction,
            command.ReasonCode, command.Description, eligibility, Guid.NewGuid());
        return await disputeStore.AddAsync(dispute, cancellationToken) == DisputeAddResult.Added
            ? new SubmitDisputeResult.Created(DisputeView.From(dispute))
            : new SubmitDisputeResult.ActiveDisputeExists();
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
