using ClearCase.Infrastructure.Transactions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Api.IntegrationTests;

/// <summary>An isolated SQLite file for relational persistence tests.</summary>
internal sealed class TemporaryDatabase : IAsyncDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"clearcase-test-{Guid.NewGuid():N}.db");

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
