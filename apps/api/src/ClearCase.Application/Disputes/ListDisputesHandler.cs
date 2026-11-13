using ClearCase.Application.Transactions;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;

namespace ClearCase.Application.Disputes;

public sealed record DisputeCursor(DateTimeOffset CreatedAt, string Reference);
public sealed record ListDisputesQuery(int PageSize, DisputeCursor? After);
public sealed record DisputeListItem(Guid Id, string Reference, Guid TransactionId, TransactionCategory Category,
    DisputeCaseStatus CaseStatus, DisputeDecisionStatus DecisionStatus, DisputeFinancialStatus FinancialStatus, DateTimeOffset CreatedAt);
public sealed record DisputeListPage(IReadOnlyList<DisputeListItem> Items, bool HasNextPage);

public interface IListDisputesHandler
{
    Task<DisputeListPage> HandleAsync(ListDisputesQuery query, CancellationToken cancellationToken);
}

public sealed class ListDisputesHandler(ICustomerContext customer, IDisputeStore store) : IListDisputesHandler
{
    public Task<DisputeListPage> HandleAsync(ListDisputesQuery query, CancellationToken cancellationToken) =>
        store.ReadPageAsync(customer.CustomerId, query, cancellationToken);
}
