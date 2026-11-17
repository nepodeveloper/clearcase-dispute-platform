using ClearCase.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Api.IntegrationTests;

/// <summary>Ensure checked-in migration snapshot matches the runnable EF Core model.</summary>
public sealed class MigrationModelCompatibilityTests
{
    [Fact]
    public async Task CleanDatabaseMigratesFullyAndHasNoPendingModelChanges()
    {
        await using var database = new TemporaryDatabase();
        await using var db = database.CreateContext();
        await db.Database.MigrateAsync();

        Assert.False(db.Database.HasPendingModelChanges(),
            "The EF model has changed without an additive migration and updated snapshot.");
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
}
