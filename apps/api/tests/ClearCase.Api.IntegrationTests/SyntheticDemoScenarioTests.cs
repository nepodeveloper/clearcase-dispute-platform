using ClearCase.Domain.Transactions;
using ClearCase.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Api.IntegrationTests;

public sealed class SyntheticDemoScenarioTests
{
    private static readonly Guid CustomerId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Theory]
    [InlineData(2027, 5, 17)]
    [InlineData(2032, 12, 1)]
    public async Task FreshDemoSeedsAreTimeRelativeEligibleAndRepeatable(
        int year, int month, int day)
    {
        await using var database = new TemporaryDatabase();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        var instant = new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero);
        var clock = new FrozenClock(instant);

        await SyntheticTransactionSeeder.SeedAsync(context, default, clock);
        var all = await context.Transactions.AsNoTracking()
            .Where(item => item.CustomerId == CustomerId)
            .OrderBy(item => item.Id).ToArrayAsync();
        Assert.Equal(30, all.Length);
        Assert.Equal(10, all.Count(item => item.Type == TransactionType.CardPurchase));
        Assert.Equal(10, all.Count(item => item.Type == TransactionType.DebitOrder));
        Assert.Equal(10, all.Count(item => item.Type == TransactionType.BankCharge));

        var anchor = instant.Date.AddDays(-1);
        foreach (var item in all.Where(item => item.Status == TransactionStatus.Posted))
        {
            Assert.InRange((anchor - item.TransactionDate).TotalDays, -1, 31);
            Assert.True((instant - item.TransactionDate).TotalDays < 60,
                "The seeded payment must remain in both 60-day and 120-day demo windows.");
        }
        Assert.True(all[0].Status == TransactionStatus.Posted);
        Assert.Contains(all, item => item.Type == TransactionType.DebitOrder && item.MandateAuthorised == true);
        Assert.Contains(all, item => item.Type == TransactionType.BankCharge && item.Status == TransactionStatus.Posted);
        Assert.Contains(all, item => item.Status == TransactionStatus.PendingAuthorization);

        // Seed is insert-only: running it with a different clock must not rewrite cases
        // already referring to these stable synthetic transaction IDs.
        await SyntheticTransactionSeeder.SeedAsync(context, default,
            new FrozenClock(instant.AddYears(1)));
        Assert.Equal(31, await context.Transactions.AsNoTracking().CountAsync()); // 30 + isolated other customer
        var retained = await context.Transactions.AsNoTracking()
            .SingleAsync(item => item.Id == Guid.Parse("00000000-0000-0000-0000-000000000001"));
        Assert.Equal(all[0].TransactionDate, retained.TransactionDate);
    }

    private sealed class FrozenClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
