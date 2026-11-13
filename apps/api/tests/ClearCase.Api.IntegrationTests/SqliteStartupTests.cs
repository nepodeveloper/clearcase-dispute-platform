using System.Net;
using System.Text.Json;
using ClearCase.Infrastructure.Transactions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClearCase.Api.IntegrationTests;

public sealed class SqliteStartupTests
{
    [Fact]
    public async Task DevelopmentStartup_CreatesMigratesAndRepeatSeedsDatabase()
    {
        var path = NewPath();
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                await using var factory = CreateFactory(path, "Development");
                using var client = factory.CreateClient();
                using var response = await client.GetAsync("/v1/transactions?pageSize=100");
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(30, body.RootElement.GetProperty("totalCount").GetInt32());
            }

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString();
            await using var context = new TransactionDbContext(
                new DbContextOptionsBuilder<TransactionDbContext>().UseSqlite(connectionString).Options);
            Assert.Equal(4, (await context.Database.GetAppliedMigrationsAsync()).Count());
            Assert.Equal(31, await context.Transactions.CountAsync());
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task ProductionStartup_DoesNotCreateOrMigrateDatabase()
    {
        var path = NewPath();
        try
        {
            await using var factory = CreateFactory(path, "Production");
            using var client = factory.CreateClient();
            using var response = await client.GetAsync("/health/live");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(File.Exists(path));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task ProductionStartup_LeavesExistingSchemaAndDataUnchanged()
    {
        var path = NewPath();
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString();
            var options = new DbContextOptionsBuilder<TransactionDbContext>().UseSqlite(connectionString).Options;
            await using (var context = new TransactionDbContext(options))
                await context.GetService<IMigrator>().MigrateAsync("20261112180532_InitialTransactions");

            await using (var factory = CreateFactory(path, "Production"))
            {
                using var client = factory.CreateClient();
                using var response = await client.GetAsync("/health/live");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            await using var verify = new TransactionDbContext(options);
            Assert.Single(await verify.Database.GetAppliedMigrationsAsync());
            Assert.Equal(0, await verify.Transactions.CountAsync());
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task MissingDatabase_ReturnsSanitizedProblemDetailsAndCorrelation()
    {
        var path = NewPath();
        try
        {
            await using var factory = CreateFactory(path, "Testing");
            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/transactions");
            request.Headers.Add("X-Correlation-ID", "database-unavailable");
            using var response = await client.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            using var body = JsonDocument.Parse(content);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("database-unavailable", body.RootElement.GetProperty("correlationId").GetString());
            Assert.DoesNotContain(path, content);
            Assert.DoesNotContain("SQLite", content);
            Assert.False(File.Exists(path));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task Configuration_ResolvesContentRootAndEnablesForeignKeysAndTimeout()
    {
        var path = NewPath();
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Persistence:DatabasePath"] = Path.GetFileName(path),
                    ["Persistence:BusyTimeoutSeconds"] = "5"
                }).Build();
            var services = new ServiceCollection();
                services.AddSingleton<IConfiguration>(configuration);
                services.AddTransactionPersistence(Path.GetDirectoryName(path)!, null, true);
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
            await context.Database.OpenConnectionAsync();
            var connection = Assert.IsType<SqliteConnection>(context.Database.GetDbConnection());
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys;";

            Assert.Equal(path, connection.DataSource);
            Assert.Equal(5, connection.DefaultTimeout);
            Assert.Equal(1L, await command.ExecuteScalarAsync());
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public void Configuration_RejectsDatabaseInsideWebRoot()
    {
        var root = Path.GetTempPath();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Persistence:DatabasePath"] = Path.Combine("wwwroot", "clearcase.db"),
                ["Persistence:BusyTimeoutSeconds"] = "5"
            }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddTransactionPersistence(root, null, true);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Throws<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<TransactionDbContext>());
    }

    private static WebApplicationFactory<Program> CreateFactory(string path, string environment) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Persistence:DatabasePath"] = path }));
        });

    private static string NewPath() =>
        Path.Combine(Path.GetTempPath(), $"clearcase-startup-test-{Guid.NewGuid():N}.db");

    private static void DeleteDatabase(string path)
    {
        File.Delete(path);
        File.Delete(path + "-wal");
        File.Delete(path + "-shm");
        File.Delete(path + "-journal");
    }
}
