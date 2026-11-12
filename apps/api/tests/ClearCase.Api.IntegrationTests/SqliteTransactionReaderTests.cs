using ClearCase.Application.Transactions;
using ClearCase.Domain.Transactions;
using ClearCase.Infrastructure.Transactions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ClearCase.Api.IntegrationTests;

public sealed class SqliteTransactionReaderTests
{
    private static readonly Guid DemoCustomer = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherCustomer = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherTransaction = Guid.Parse("00000000-0000-0000-0000-000000000099");

    [Fact]
    public async Task MigrationsAndRepeatSeed_PersistAcrossContextRestarts()
    {
        await using var database = new TemporaryDatabase();
        await using (var context = database.CreateContext())
        {
            await context.Database.MigrateAsync();
            await SyntheticTransactionSeeder.SeedAsync(context, CancellationToken.None);
            await SyntheticTransactionSeeder.SeedAsync(context, CancellationToken.None);

            Assert.Equal(2, (await context.Database.GetAppliedMigrationsAsync()).Count());
            Assert.Equal(31, await context.Transactions.CountAsync());
        }

        await using var restarted = database.CreateContext();
        Assert.Equal(31, await restarted.Transactions.CountAsync());
        var reader = new SqliteTransactionReader(restarted);
        var details = await reader.ReadByIdAsync(
            DemoCustomer, Guid.Parse("00000000-0000-0000-0000-000000000001"), CancellationToken.None);
        Assert.NotNull(details);
        Assert.Equal("Woolworths", details.Description);
        Assert.Equal(750.00m, details.Amount);
    }

    [Fact]
    public async Task Reader_ScopesFiltersAndOrdersBeforePagination()
    {
        await using var database = new TemporaryDatabase();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        await SyntheticTransactionSeeder.SeedAsync(context, CancellationToken.None);
        var reader = new SqliteTransactionReader(context);

        var page = await reader.ReadAsync(
            DemoCustomer, new ListTransactionsQuery(2, 2, null, null, null), CancellationToken.None);
        var debitOrders = await reader.ReadAsync(
            DemoCustomer, new ListTransactionsQuery(1, 100, TransactionType.DebitOrder, null, null),
            CancellationToken.None);
        var otherCustomer = await reader.ReadAsync(
            OtherCustomer, new ListTransactionsQuery(1, 100, null, null, null), CancellationToken.None);

        Assert.Equal(30, page.TotalCount);
        Assert.Equal(
            new[] { "00000000-0000-0000-0000-000000000003", "00000000-0000-0000-0000-000000000004" },
            page.Items.Select(item => item.Id.ToString()).ToArray());
        Assert.Equal(10, debitOrders.TotalCount);
        Assert.All(debitOrders.Items, item => Assert.Equal("DebitOrder", item.TransactionType));
        Assert.Single(otherCustomer.Items);
        Assert.Equal(OtherTransaction, otherCustomer.Items[0].Id);
        Assert.Null(await reader.ReadByIdAsync(DemoCustomer, OtherTransaction, CancellationToken.None));
        Assert.Null(await reader.ReadForEligibilityAsync(DemoCustomer, OtherTransaction, CancellationToken.None));
    }

    [Fact]
    public async Task UpgradeFromInitialMigration_PreservesExistingTransactions()
    {
        await using var database = new TemporaryDatabase();
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261112180532_InitialTransactions");
        await SyntheticTransactionSeeder.SeedAsync(context, CancellationToken.None);

        await migrator.MigrateAsync();

        Assert.Equal(31, await context.Transactions.CountAsync());
        var reader = new SqliteTransactionReader(context);
        Assert.NotNull(await reader.ReadByIdAsync(
            DemoCustomer, Guid.Parse("00000000-0000-0000-0000-000000000001"), CancellationToken.None));
    }

    [Fact]
    public async Task Reader_RoundTripsExactMoneyUtcAndEligibilityFacts()
    {
        await using var database = new TemporaryDatabase();
        var id = Guid.Parse("00000000-0000-0000-0000-000000000090");
        await using (var context = database.CreateContext())
        {
            await context.Database.MigrateAsync();
            context.Transactions.Add(Transaction(id, 199999));
            await context.SaveChangesAsync();
        }

        await using var restarted = database.CreateContext();
        var reader = new SqliteTransactionReader(restarted);
        var details = await reader.ReadByIdAsync(DemoCustomer, id, CancellationToken.None);
        var eligibility = await reader.ReadForEligibilityAsync(DemoCustomer, id, CancellationToken.None);
        var list = await reader.ReadAsync(
            DemoCustomer, new ListTransactionsQuery(1, 10, null, null, null), CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal(1999.99m, details.Amount);
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 8, 30, 0, TimeSpan.Zero), details.TransactionDate);
        Assert.Equal(TimeSpan.Zero, details.TransactionDate.Offset);
        Assert.False(details.IsDisputable);
        var listed = Assert.Single(list.Items);
        Assert.Equal("1999.99", listed.Amount);
        Assert.False(listed.IsDisputable);
        Assert.NotNull(eligibility);
        Assert.Equal(1999.99m, eligibility.Amount);
        Assert.Equal(DebitOrderMandateType.RegisteredMandate, eligibility.MandateType);
        Assert.True(eligibility.MandateAuthorised);
        Assert.True(eligibility.MandateMatchesCollection);
    }

    [Fact]
    public async Task TransactionRollback_DoesNotPersistWrites()
    {
        await using var database = new TemporaryDatabase();
        var id = Guid.Parse("00000000-0000-0000-0000-000000000091");
        await using (var context = database.CreateContext())
        {
            await context.Database.MigrateAsync();
            await using var transaction = await context.Database.BeginTransactionAsync();
            context.Transactions.Add(Transaction(id, 200001));
            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var restarted = database.CreateContext();
        Assert.False(await restarted.Transactions.AnyAsync(item => item.Id == id));
    }

    [Fact]
    public async Task DuplicateTransactionId_IsRejectedByDatabase()
    {
        await using var database = new TemporaryDatabase();
        var id = Guid.Parse("00000000-0000-0000-0000-000000000092");
        await using (var context = database.CreateContext())
        {
            await context.Database.MigrateAsync();
            context.Transactions.Add(Transaction(id, 100));
            await context.SaveChangesAsync();
        }

        await using var duplicate = database.CreateContext();
        duplicate.Transactions.Add(Transaction(id, 100));
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
    }

    [Fact]
    public async Task RepeatSeed_DoesNotOverwriteExistingRecords()
    {
        await using var database = new TemporaryDatabase();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        await SyntheticTransactionSeeder.SeedAsync(context, CancellationToken.None);
        var item = await context.Transactions.SingleAsync(
            transaction => transaction.Id == Guid.Parse("00000000-0000-0000-0000-000000000001"));
        item.Description = "Updated synthetic description";
        await context.SaveChangesAsync();

        await SyntheticTransactionSeeder.SeedAsync(context, CancellationToken.None);
        context.ChangeTracker.Clear();
        Assert.Equal("Updated synthetic description", (await context.Transactions.SingleAsync(
            transaction => transaction.Id == item.Id)).Description);
    }

    [Fact]
    public async Task NonZarCurrency_IsRejectedByDatabase()
    {
        await using var database = new TemporaryDatabase();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        var item = Transaction(Guid.NewGuid(), 100);
        item.Currency = "JPY";
        context.Transactions.Add(item);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task EmptyMigratedDatabase_ReturnsEmptyResults()
    {
        await using var database = new TemporaryDatabase();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        var reader = new SqliteTransactionReader(context);

        var page = await reader.ReadAsync(
            DemoCustomer, new ListTransactionsQuery(1, 20, null, null, null), CancellationToken.None);
        var details = await reader.ReadByIdAsync(DemoCustomer, Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(0, page.TotalPages);
        Assert.Null(details);
    }

    private static PersistedTransaction Transaction(Guid id, long amountMinorUnits) => new()
    {
        Id = id,
        CustomerId = DemoCustomer,
        Type = TransactionType.DebitOrder,
        Description = "Synthetic collection",
        AmountMinorUnits = amountMinorUnits,
        Currency = "ZAR",
        TransactionDate = new DateTimeOffset(2026, 11, 10, 10, 30, 0, TimeSpan.FromHours(2)),
        Status = TransactionStatus.Posted,
        Reference = "SYNTHETIC",
        IsDisputable = false,
        IsDebit = true,
        HasRequiredDisputeInformation = true,
        CollectionDate = new DateOnly(2026, 11, 10),
        MandateType = DebitOrderMandateType.RegisteredMandate,
        MandateAuthorised = true,
        MandateMatchesCollection = true
    };

    private sealed class TemporaryDatabase : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(), $"clearcase-pr05-{Guid.NewGuid():N}.db");

        public TransactionDbContext CreateContext()
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                ForeignKeys = true,
                DefaultTimeout = 5,
                Pooling = false
            }.ToString();
            return new TransactionDbContext(
                new DbContextOptionsBuilder<TransactionDbContext>().UseSqlite(connectionString).Options);
        }

        public ValueTask DisposeAsync()
        {
            File.Delete(_path);
            File.Delete(_path + "-wal");
            File.Delete(_path + "-shm");
            File.Delete(_path + "-journal");
            return ValueTask.CompletedTask;
        }
    }
}
