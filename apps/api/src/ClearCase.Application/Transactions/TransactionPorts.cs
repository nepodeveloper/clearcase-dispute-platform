using ClearCase.Domain.Transactions;

namespace ClearCase.Application.Transactions;

public interface ICustomerContext
{
    Guid CustomerId { get; }
}

public interface ITransactionReader
{
    Task<TransactionListResponse> ReadAsync(
        Guid customerId,
        ListTransactionsQuery query,
        CancellationToken cancellationToken);

    Task<TransactionDetails?> ReadByIdAsync(
        Guid customerId,
        Guid transactionId,
        CancellationToken cancellationToken);

    Task<DisputeTransaction?> ReadForEligibilityAsync(
        Guid customerId,
        Guid transactionId,
        CancellationToken cancellationToken);
}

public interface IGetTransactionDetailsHandler
{
    Task<TransactionDetails?> HandleAsync(Guid transactionId, CancellationToken cancellationToken);
}

public interface IListTransactionsHandler
{
    Task<TransactionListResponse> HandleAsync(
        ListTransactionsQuery query,
        CancellationToken cancellationToken);
}
