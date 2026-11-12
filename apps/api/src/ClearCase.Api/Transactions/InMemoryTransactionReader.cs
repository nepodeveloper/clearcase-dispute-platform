using System.Globalization;

namespace ClearCase.Api.Transactions;

public sealed class InMemoryTransactionReader : ITransactionReader
{
    private static readonly Guid DemoCustomer = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherCustomer = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly string[] Descriptions = ["Woolworths", "Monthly insurance", "Account service fee"];
    private static readonly decimal[] Amounts = [750.00m, 420.50m, 15.00m];
    private static readonly DateTimeOffset Start = new(2026, 11, 10, 8, 30, 0, TimeSpan.Zero);
    private static readonly IReadOnlyList<Transaction> Transactions = CreateTransactions();

    public Task<TransactionListResponse> ReadAsync(Guid customerId, TransactionQuery query, CancellationToken cancellationToken)
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
                item.Date, "Posted", true)).ToArray();

        return Task.FromResult(new TransactionListResponse(
            items, query.PageNumber, query.PageSize, matching.Length, totalPages, query.PageNumber < totalPages));
    }

    private static IReadOnlyList<Transaction> CreateTransactions()
    {
        var items = Enumerable.Range(1, 30).Select(index =>
        {
            var typeIndex = (index - 1) % 3;
            return new Transaction(
                Guid.Parse($"00000000-0000-0000-0000-{index:000000000000}"),
                DemoCustomer, (TransactionType)typeIndex, Descriptions[typeIndex], Amounts[typeIndex],
                Start.AddDays(-Math.Min(index - 1, 28)));
        }).ToList();
        items.Add(new Transaction(Guid.Parse("00000000-0000-0000-0000-000000000099"),
            OtherCustomer, TransactionType.CardPurchase, "Other synthetic customer", 25m, Start));
        return items;
    }

    private sealed record Transaction(
        Guid Id, Guid CustomerId, TransactionType Type, string Description, decimal Amount, DateTimeOffset Date);
}
