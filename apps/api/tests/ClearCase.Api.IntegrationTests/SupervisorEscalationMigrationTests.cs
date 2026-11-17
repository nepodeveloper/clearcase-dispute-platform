using ClearCase.Application.Transactions;
using ClearCase.Domain.DisputeEligibility;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;
using ClearCase.Infrastructure.Disputes;
using ClearCase.Infrastructure.SupervisorEscalations;
using ClearCase.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ClearCase.Api.IntegrationTests;

public sealed class SupervisorEscalationMigrationTests
{
    private const string PreviousMigration = "20261116184208_AddDisputeCommandAudit";
    private static readonly Guid CustomerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Instant =
        new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UpgradeAndDowngradePreserveEscalationsAndRestrictMixedActiveStatuses()
    {
        await using var database = new TemporaryDatabase();
        await using var db = database.CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        var disputeId = await SeedDisputeAsync(db);
        var original = Escalation(disputeId, DisputeEscalationStatus.Open);
        db.DisputeEscalations.Add(original);
        await db.SaveChangesAsync();

        await migrator.MigrateAsync();
        Assert.Equal(18, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Equal(original.Id,
            (await db.DisputeEscalations.AsNoTracking().SingleAsync(e => e.DisputeId == disputeId)).Id);

        // Different active statuses may not coexist for one dispute.
        db.DisputeEscalations.Add(Escalation(disputeId, DisputeEscalationStatus.Acknowledged));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        // Resolved records are historical and do not consume the single active slot.
        db.DisputeEscalations.Add(Escalation(disputeId, DisputeEscalationStatus.Resolved));
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.DisputeEscalations.CountAsync(e => e.DisputeId == disputeId));

        await migrator.MigrateAsync(PreviousMigration);
        Assert.Equal(14, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Equal(2, await db.DisputeEscalations.CountAsync(e => e.DisputeId == disputeId));
        Assert.True(await db.Disputes.AnyAsync(e => e.Id == disputeId));
    }

    [Fact]
    public async Task MigrationRejectsLegacyMixedActiveDuplicatesWithoutDeletingHistory()
    {
        await using var database = new TemporaryDatabase();
        await using var db = database.CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        var disputeId = await SeedDisputeAsync(db);

        // Old index is unique on (DisputeId, Status), so this was formerly possible.
        db.DisputeEscalations.Add(Escalation(disputeId, DisputeEscalationStatus.Open));
        db.DisputeEscalations.Add(Escalation(disputeId, DisputeEscalationStatus.Acknowledged));
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.DisputeEscalations.CountAsync(e => e.DisputeId == disputeId));

        var error = await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync());
        Assert.Contains("UNIQUE", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(14, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Equal(2, await db.DisputeEscalations.AsNoTracking().CountAsync(e => e.DisputeId == disputeId));
    }

    [Fact]
    public async Task CompetingWritesCannotCreateTwoActiveEscalations()
    {
        await using var database = new TemporaryDatabase();
        Guid disputeId;
        await using (var setup = database.CreateContext())
        {
            await setup.Database.MigrateAsync();
            disputeId = await SeedDisputeAsync(setup);
        }

        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        first.DisputeEscalations.Add(Escalation(disputeId, DisputeEscalationStatus.Open));
        second.DisputeEscalations.Add(Escalation(disputeId, DisputeEscalationStatus.Acknowledged));

        // Both contexts attempt independent writes. SQLite may reject a competing writer
        // with a constraint or lock error, but two active records must never be committed.
        var attempts = await Task.WhenAll(
            AttemptAsync(first), AttemptAsync(second));
        Assert.Equal(1, attempts.Count(success => success));
        await using var verify = database.CreateContext();
        Assert.Single(await verify.DisputeEscalations.AsNoTracking()
            .Where(e => e.DisputeId == disputeId && e.Status != DisputeEscalationStatus.Resolved)
            .ToArrayAsync());
    }

    private static async Task<bool> AttemptAsync(TransactionDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    private static async Task<Guid> SeedDisputeAsync(TransactionDbContext db)
    {
        var disputeId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        db.Transactions.Add(new PersistedTransaction
        {
            Id = transactionId,
            CustomerId = CustomerId,
            Type = TransactionType.CardPurchase,
            Description = "Synthetic migration verification purchase",
            AmountMinorUnits = 1234,
            Currency = "ZAR",
            TransactionDate = Instant,
            Status = TransactionStatus.Posted,
            IsDisputable = true,
            IsDebit = true,
            HasRequiredDisputeInformation = true
        });
        db.Disputes.Add(new PersistedDispute
        {
            Id = disputeId,
            Reference = "DSP-MIGRATION-" + disputeId.ToString("N"),
            CustomerId = CustomerId,
            TransactionId = transactionId,
            Category = TransactionCategory.CardPurchase,
            ReasonCode = "unrecognisedTransaction",
            Description = "Synthetic migration verification dispute.",
            CaseStatus = DisputeCaseStatus.ManualReview,
            DecisionStatus = DisputeDecisionStatus.Pending,
            FinancialStatus = DisputeFinancialStatus.NotInitiated,
            EligibilityOutcome = DisputeEligibilityOutcome.EligibleToSubmit,
            EligibilityPolicyVersion = "synthetic-test-v1",
            Version = 2,
            CreatedAt = Instant
        });
        await db.SaveChangesAsync();
        return disputeId;
    }

    private static PersistedDisputeEscalation Escalation(Guid disputeId, DisputeEscalationStatus status)
    {
        var id = Guid.NewGuid();
        return new PersistedDisputeEscalation
        {
            Id = id,
            Reference = "ESC-" + id.ToString("N").ToUpperInvariant(),
            DisputeId = disputeId,
            Reason = "Additional investigation required",
            Status = status,
            CreatedByEmployeeId = Guid.NewGuid(),
            CreatedAt = Instant,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            RequestFingerprint = new string('A', 64)
        };
    }
}
