namespace ClearCase.Api.Transactions;

public interface ITransactionReader
{
    Task<TransactionListResponse> ReadAsync(Guid customerId, TransactionQuery query, CancellationToken cancellationToken);
}
