using ClearCase.Application.Disputes;
using ClearCase.Domain.Disputes;

namespace ClearCase.Infrastructure.Disputes;

public sealed class MockBankChargeAdjustmentGateway(
    BankChargeAdjustmentOutcome outcome = BankChargeAdjustmentOutcome.Confirmed)
    : IBankChargeAdjustmentGateway
{
    public Task<BankChargeAdjustmentOutcome> AdjustAsync(
        string operationReference, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationReference);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(outcome);
    }
}
