using System.Globalization;
using ClearCase.Application.Transactions;
using ClearCase.Domain.Transactions;

namespace ClearCase.Api.Transactions;

public sealed class InMemoryTransactionReader : ITransactionReader
{
    private static readonly Guid DemoCustomer = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherCustomer = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly string[] Descriptions = ["Woolworths", "Monthly insurance", "Account service fee"];
    private static readonly decimal[] Amounts = [750.00m, 420.50m, 15.00m];
    private static readonly DateTimeOffset Start = new(2026, 11, 10, 8, 30, 0, TimeSpan.Zero);
    private static readonly IReadOnlyList<Transaction> Transactions = CreateTransactions();

    public Task<TransactionListResponse> ReadAsync(
        Guid customerId,
        ListTransactionsQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var matching = Transactions
            .Where(item => item.CustomerId == customerId)
            .Where(item => query.Type is null || item.Type == query.Type)
            .Where(item => query.FromDate is null || DateOnly.FromDateTime(item.Date.UtcDateTime) >= query.FromDate)
            .Where(item => query.ToDate is null || DateOnly.FromDateTime(item.Date.UtcDateTime) <= query.ToDate)
            .OrderByDescending(item => item.Date)
            .ThenBy(item => item.Id)
            .ToArray();

        var totalPages = (matching.Length + query.PageSize - 1) / query.PageSize;
        var offset = ((long)query.PageNumber - 1) * query.PageSize;
        var items = offset >= matching.Length
            ? []
            : matching.Skip((int)offset).Take(query.PageSize).Select(item => new TransactionResponse(
                item.Id, item.Type.ToString(), item.Description,
                item.Amount.ToString("0.00", CultureInfo.InvariantCulture), "ZAR",
                item.Date, item.Status.ToString(), true)).ToArray();

        return Task.FromResult(new TransactionListResponse(
            items, query.PageNumber, query.PageSize, matching.Length, totalPages, query.PageNumber < totalPages));
    }

    public Task<TransactionDetails?> ReadByIdAsync(
        Guid customerId,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var item = Transactions.SingleOrDefault(transaction =>
            transaction.CustomerId == customerId && transaction.Id == transactionId);
        return Task.FromResult(item is null ? null : new TransactionDetails(
            item.Id, item.Type.ToString(), item.Description, item.MerchantName, item.Amount,
            "ZAR", item.Date, item.Status.ToString(), item.Reference, true));
    }

    public Task<DisputeTransaction?> ReadForEligibilityAsync(
        Guid customerId,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var item = Transactions.SingleOrDefault(transaction =>
            transaction.CustomerId == customerId && transaction.Id == transactionId);
        if (item is null)
            return Task.FromResult<DisputeTransaction?>(null);

        return Task.FromResult<DisputeTransaction?>(new DisputeTransaction(
            item.Id,
            item.Type switch
            {
                TransactionType.CardPurchase => TransactionCategory.CardPurchase,
                TransactionType.DebitOrder => TransactionCategory.DebitOrder,
                TransactionType.BankCharge => TransactionCategory.BankCharge,
                _ => throw new InvalidOperationException("Unsupported synthetic transaction type.")
            },
            item.Amount,
            "ZAR",
            item.Date,
            item.Status,
            item.IsDebit,
            item.HasRequiredDisputeInformation,
            item.CollectionDate,
            item.MandateType,
            item.MandateAuthorised,
            item.MandateMatchesCollection));
    }

    private static IReadOnlyList<Transaction> CreateTransactions()
    {
        var items = Enumerable.Range(1, 30).Select(index =>
        {
            var typeIndex = (index - 1) % 3;
            var date = Start.AddDays(-Math.Min(index - 1, 28));
            var type = (TransactionType)typeIndex;
            var isDebitOrder = type == TransactionType.DebitOrder;
            return new Transaction(
                Guid.Parse($"00000000-0000-0000-0000-{index:000000000000}"),
                DemoCustomer, type, Descriptions[typeIndex],
                index == 5 ? 2000.01m : Amounts[typeIndex],
                date,
                typeIndex == 2 ? null : Descriptions[typeIndex], $"TXN-2026-{index:000000}",
                index == 4 ? TransactionStatus.PendingAuthorization : TransactionStatus.Posted,
                IsDebit: true,
                HasRequiredDisputeInformation: index != 8,
                CollectionDate: isDebitOrder ? DateOnly.FromDateTime(date.UtcDateTime) : null,
                MandateType: isDebitOrder
                    ? index == 11 ? DebitOrderMandateType.DebiCheck
                        : index == 8 ? null : DebitOrderMandateType.EftDebitOrder
                    : null,
                MandateAuthorised: isDebitOrder ? index == 8 ? null : true : null,
                MandateMatchesCollection: isDebitOrder ? index == 8 ? null : true : null);
        }).ToList();
        items.Add(new Transaction(Guid.Parse("00000000-0000-0000-0000-000000000099"),
            OtherCustomer, TransactionType.CardPurchase, "Other synthetic customer", 25m, Start,
            "Other synthetic customer", "TXN-2026-000099", TransactionStatus.Posted, true, true,
            null, null, null, null));
        return items;
    }

    private sealed record Transaction(
        Guid Id, Guid CustomerId, TransactionType Type, string Description, decimal Amount,
        DateTimeOffset Date, string? MerchantName, string? Reference, TransactionStatus Status,
        bool IsDebit, bool HasRequiredDisputeInformation, DateOnly? CollectionDate,
        DebitOrderMandateType? MandateType, bool? MandateAuthorised, bool? MandateMatchesCollection);
}
