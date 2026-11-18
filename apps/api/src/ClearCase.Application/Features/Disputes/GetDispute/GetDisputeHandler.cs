using ClearCase.Application.Transactions;

namespace ClearCase.Application.Disputes;

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
