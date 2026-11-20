using ClearCase.Application.AgentDisputes;
using ClearCase.Application.Transactions;
using ClearCase.Domain.Disputes;

namespace ClearCase.Application.Disputes;

public sealed record WithdrawDisputeCommand(
    Guid DisputeId, long ExpectedVersion, string? Reason, string CorrelationId);

public interface IDisputeWithdrawalStore
{
    Task<DisputeView?> WithdrawAsync(
        Guid customerId, WithdrawDisputeCommand command, DateTimeOffset occurredAt,
        CancellationToken cancellationToken);
}

public interface IWithdrawDisputeHandler
{
    Task<DisputeView?> HandleAsync(WithdrawDisputeCommand command, CancellationToken cancellationToken);
}

public sealed class WithdrawDisputeHandler(
    ICustomerContext customerContext, IDisputeWithdrawalStore store, TimeProvider clock)
    : IWithdrawDisputeHandler
{
    public async Task<DisputeView?> HandleAsync(
        WithdrawDisputeCommand command, CancellationToken cancellationToken)
    {
        try
        {
            return await store.WithdrawAsync(
                customerContext.CustomerId, command, clock.GetUtcNow(), cancellationToken);
        }
        catch (ActiveDisputeEscalationException)
        {
            // Supervisor review owns the case; from the customer's side this is simply not withdrawable.
            throw new DisputeWithdrawalNotAllowedException();
        }
    }
}
