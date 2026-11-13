using ClearCase.Domain.Transactions;

namespace ClearCase.Application.DisputeEligibility;

public interface IDebitOrderMandateReader
{
    Task<DebitOrderMandate?> ReadAsync(
        Guid customerId, Guid transactionId, CancellationToken cancellationToken);
}
