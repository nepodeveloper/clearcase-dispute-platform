using ClearCase.Infrastructure.Transactions;
using ClearCase.Infrastructure.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ClearCase.Api.IntegrationTests;

public sealed class SqliteWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"clearcase-api-test-{Guid.NewGuid():N}.db");

    public async Task InitializeAsync()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            DefaultTimeout = 5,
            Pooling = false
        }.ToString();
        await using var context = new TransactionDbContext(
            new DbContextOptionsBuilder<TransactionDbContext>().UseSqlite(connectionString).Options);
        await context.Database.MigrateAsync();
        await SyntheticTransactionSeeder.SeedAsync(context, CancellationToken.None);
        await SyntheticIdentitySeeder.SeedAsync(context, TestIdentityPasswords.Seed, CancellationToken.None);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Persistence:DatabasePath"] = _path,
                // Legacy workflow tests opt in; real auth tests override this, and Development ignores it.
                ["Authentication:EnableDevelopmentCompatibilityPath"] = "true"
            }));

    async Task IAsyncLifetime.DisposeAsync() => await DisposeAsync();

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        File.Delete(_path);
        File.Delete(_path + "-wal");
        File.Delete(_path + "-shm");
        File.Delete(_path + "-journal");
    }
}
