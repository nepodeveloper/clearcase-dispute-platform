namespace ClearCase.Application.Transactions;

public sealed class ListTransactionsHandler(
    ICustomerContext customerContext,
    ITransactionReader transactionReader) : IListTransactionsHandler
{
    public Task<TransactionListResponse> HandleAsync(
        ListTransactionsQuery query,
        CancellationToken cancellationToken) =>
        transactionReader.ReadAsync(customerContext.CustomerId, query, cancellationToken);
}
