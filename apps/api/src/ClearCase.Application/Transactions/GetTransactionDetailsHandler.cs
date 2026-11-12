namespace ClearCase.Application.Transactions;

public sealed class GetTransactionDetailsHandler(
    ICustomerContext customerContext,
    ITransactionReader transactionReader) : IGetTransactionDetailsHandler
{
    public Task<TransactionDetails?> HandleAsync(Guid transactionId, CancellationToken cancellationToken) =>
        transactionReader.ReadByIdAsync(customerContext.CustomerId, transactionId, cancellationToken);
}
