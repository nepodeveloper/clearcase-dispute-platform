using System.Globalization;
using ClearCase.Application.Transactions;
using ClearCase.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.Transactions;

public sealed class SqliteTransactionReader(TransactionDbContext context) : ITransactionReader
{
    public async Task<TransactionListResponse> ReadAsync(
        Guid customerId, ListTransactionsQuery query, CancellationToken cancellationToken)
    {
        var matching = context.Transactions.AsNoTracking().Where(item => item.CustomerId == customerId);
        if (query.Type is not null)
            matching = matching.Where(item => item.Type == query.Type);
        if (query.FromDate is not null)
        {
            var from = new DateTimeOffset(query.FromDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            matching = matching.Where(item => item.TransactionDate >= from);
        }
        if (query.ToDate is not null)
        {
            var to = new DateTimeOffset(query.ToDate.Value.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            matching = matching.Where(item => item.TransactionDate <= to);
        }

        var totalCount = await matching.CountAsync(cancellationToken);
        var totalPages = (int)(((long)totalCount + query.PageSize - 1) / query.PageSize);
        var offset = ((long)query.PageNumber - 1) * query.PageSize;
        var records = offset >= totalCount
            ? []
            : await matching.OrderByDescending(item => item.TransactionDate).ThenBy(item => item.Id)
                .Skip((int)offset).Take(query.PageSize).ToArrayAsync(cancellationToken);
        var items = records.Select(item => new TransactionResponse(
            item.Id, item.Type.ToString(), item.Description,
            Amount(item).ToString("0.00", CultureInfo.InvariantCulture), item.Currency,
            item.TransactionDate, item.Status.ToString(), item.IsDisputable)).ToArray();

        return new TransactionListResponse(
            items, query.PageNumber, query.PageSize, totalCount, totalPages, query.PageNumber < totalPages);
    }

    public async Task<TransactionDetails?> ReadByIdAsync(
        Guid customerId, Guid transactionId, CancellationToken cancellationToken)
    {
        var item = await FindAsync(customerId, transactionId, cancellationToken);
        return item is null ? null : new TransactionDetails(
            item.Id, item.Type.ToString(), item.Description, item.MerchantName, Amount(item),
            item.Currency, item.TransactionDate, item.Status.ToString(), item.Reference, item.IsDisputable);
    }

    public async Task<DisputeTransaction?> ReadForEligibilityAsync(
        Guid customerId, Guid transactionId, CancellationToken cancellationToken)
    {
        var item = await FindAsync(customerId, transactionId, cancellationToken);
        return item is null ? null : new DisputeTransaction(
            item.Id,
            item.Type switch
            {
                TransactionType.CardPurchase => TransactionCategory.CardPurchase,
                TransactionType.DebitOrder => TransactionCategory.DebitOrder,
                TransactionType.BankCharge => TransactionCategory.BankCharge,
                _ => throw new InvalidOperationException("Unsupported persisted transaction type.")
            },
            Amount(item), item.Currency, item.TransactionDate, item.Status, item.IsDebit,
            item.HasRequiredDisputeInformation, item.CollectionDate, item.MandateType,
            item.MandateAuthorised, item.MandateMatchesCollection);
    }

    private Task<PersistedTransaction?> FindAsync(
        Guid customerId, Guid transactionId, CancellationToken cancellationToken) =>
        context.Transactions.AsNoTracking().SingleOrDefaultAsync(
            item => item.CustomerId == customerId && item.Id == transactionId, cancellationToken);

    private static decimal Amount(PersistedTransaction item) => item.AmountMinorUnits / 100m;
}
