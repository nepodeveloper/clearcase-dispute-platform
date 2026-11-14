using ClearCase.Application.Disputes;
using ClearCase.Domain.DisputeEligibility;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;
using ClearCase.Infrastructure.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ClearCase.Api.IntegrationTests;

public sealed class SqliteDisputeStoreTests
{
    private static readonly Guid DemoCustomer = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherCustomer = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CardTransaction = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset SubmittedAt = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Add_PersistsCaseAndInitialTimelineEventAcrossContextRestarts()
    {
        await using var database = await SeededDatabase();
        var dispute = NewDispute(DisputeEligibilityOutcome.EligibleToSubmit);
        await using (var context = database.CreateContext())
            Assert.Equal(DisputeAddResult.Added, await new SqliteDisputeStore(context).AddAsync(dispute, default));

        await using var restarted = database.CreateContext();
        var stored = await new SqliteDisputeStore(restarted).ReadAsync(DemoCustomer, dispute.Id, default);

        Assert.NotNull(stored);
        Assert.Equal(dispute.Reference, stored.Reference);
        Assert.Equal(DisputeCaseStatus.Submitted, stored.CaseStatus);
        Assert.Equal(DisputeDecisionStatus.Pending, stored.DecisionStatus);
        Assert.Equal(DisputeFinancialStatus.NotInitiated, stored.FinancialStatus);
        Assert.Equal(SubmittedAt, stored.CreatedAt);
        Assert.Equal(1, stored.Version);
        var initialEvent = Assert.Single(stored.Timeline);
        Assert.Equal(1, initialEvent.SequenceNumber);
        Assert.Equal(Dispute.SubmittedEventType, initialEvent.EventType);
        Assert.Equal(DisputeCaseStatus.Submitted, initialEvent.CaseStatus);
        Assert.Equal(SubmittedAt, initialEvent.OccurredAt);
        Assert.Null(await new SqliteDisputeStore(restarted).ReadAsync(OtherCustomer, dispute.Id, default));
    }

    [Fact]
    public async Task Add_WhenTimelineInsertFails_RollsBackTheCase()
    {
        await using var database = await SeededDatabase();
        await using (var context = database.CreateContext())
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER "TR_Test_FailEventInsert" BEFORE INSERT ON "DisputeEvents"
                BEGIN SELECT RAISE(ABORT, 'simulated timeline failure'); END;
                """);

        await using (var context = database.CreateContext())
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                new SqliteDisputeStore(context).AddAsync(NewDispute(DisputeEligibilityOutcome.EligibleToSubmit), default));

        await using var verify = database.CreateContext();
        Assert.Equal(0, await verify.Disputes.CountAsync());
        Assert.Equal(0, await verify.DisputeEvents.CountAsync());
    }

    [Fact]
    public async Task Add_WithSubmissionKey_CommitsOrRollsBackKeyWithTheCase()
    {
        await using var database = await SeededDatabase();
        var submission = new DisputeSubmission("retry-key-1", "fingerprint");
        await using (var context = database.CreateContext())
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER "TR_Test_FailEventInsert" BEFORE INSERT ON "DisputeEvents"
                BEGIN SELECT RAISE(ABORT, 'simulated timeline failure'); END;
                """);
        await using (var context = database.CreateContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => new SqliteDisputeStore(context)
                .AddAsync(NewDispute(DisputeEligibilityOutcome.EligibleToSubmit), submission, default));
        await using (var context = database.CreateContext())
        {
            Assert.Equal(0, await context.DisputeSubmissions.CountAsync());
            await context.Database.ExecuteSqlRawAsync("DROP TRIGGER \"TR_Test_FailEventInsert\"");
        }

        var dispute = NewDispute(DisputeEligibilityOutcome.EligibleToSubmit);
        await using (var context = database.CreateContext())
            Assert.Equal(DisputeAddResult.Added,
                await new SqliteDisputeStore(context).AddAsync(dispute, submission, default));

        await using var verify = database.CreateContext();
        var store = new SqliteDisputeStore(verify);
        Assert.Equal(new StoredDisputeSubmission(dispute.Id, "fingerprint"),
            await store.FindSubmissionAsync(DemoCustomer, "retry-key-1", default));
        Assert.Null(await store.FindSubmissionAsync(OtherCustomer, "retry-key-1", default));
        Assert.Null(await store.FindSubmissionAsync(DemoCustomer, "RETRY-KEY-1", default));
    }

    [Fact]
    public async Task Add_SecondOpenCaseForSameTransaction_IsRejectedByDatabaseConstraint()
    {
        await using var database = await SeededDatabase();
        await using var context = database.CreateContext();
        var store = new SqliteDisputeStore(context);
        Assert.Equal(DisputeAddResult.Added,
            await store.AddAsync(NewDispute(DisputeEligibilityOutcome.EligibleToSubmit), default));

        var duplicate = await store.AddAsync(NewDispute(DisputeEligibilityOutcome.RequiresManualReview), default);

        Assert.Equal(DisputeAddResult.ActiveDisputeExists, duplicate);
        Assert.Equal(1, await context.Disputes.CountAsync());
        Assert.Equal(1, await context.DisputeEvents.CountAsync());
    }

    [Fact]
    public async Task Add_AfterExistingCaseIsClosed_AllowsNewCase()
    {
        await using var database = await SeededDatabase();
        await using var context = database.CreateContext();
        var store = new SqliteDisputeStore(context);
        var first = NewDispute(DisputeEligibilityOutcome.EligibleToSubmit);
        await store.AddAsync(first, default);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Disputes\" SET \"CaseStatus\" = 'Closed' WHERE \"Id\" = {first.Id}");

        Assert.False(await store.HasOpenDisputeAsync(DemoCustomer, CardTransaction, default));
        Assert.Equal(DisputeAddResult.Added,
            await store.AddAsync(NewDispute(DisputeEligibilityOutcome.EligibleToSubmit), default));
    }

    [Fact]
    public async Task TimelineEvents_AreAppendOnly()
    {
        await using var database = await SeededDatabase();
        await using var context = database.CreateContext();
        await new SqliteDisputeStore(context).AddAsync(NewDispute(DisputeEligibilityOutcome.EligibleToSubmit), default);
        var stored = await context.DisputeEvents.SingleAsync();

        stored.PublicDescription = "Rewritten history";
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        await Assert.ThrowsAnyAsync<Exception>(() =>
            context.Database.ExecuteSqlRawAsync("DELETE FROM \"DisputeEvents\""));
        Assert.Equal(1, await context.DisputeEvents.CountAsync());
    }

    [Fact]
    public async Task OperationMigration_UpgradeAndBackoutPreserveLegacyCasesAndImmutableTimeline()
    {
        await using var database = new TemporaryDatabase();
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        const string previousMigration = "20261112185347_AddDisputes";
        await migrator.MigrateAsync(previousMigration);
        await SyntheticTransactionSeeder.SeedAsync(context, default);
        var id = Guid.Parse("aaaaaaaa-1111-4222-8333-444455556666");
        var eventId = Guid.Parse("bbbbbbbb-1111-4222-8333-444455556666");
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Disputes" ("Id", "Reference", "CustomerId", "TransactionId", "Category",
                "ReasonCode", "Description", "CaseStatus", "DecisionStatus", "FinancialStatus",
                "EligibilityOutcome", "EligibilityPolicyVersion", "CreatedAt")
            VALUES ({id}, 'DSP-LEGACY', {DemoCustomer}, {CardTransaction}, 'CardPurchase',
                'duplicateCharge', 'This is a legacy dispute case.', 'Submitted', 'Pending', 'NotInitiated',
                'EligibleToSubmit', 'demo-v1', {SubmittedAt.UtcTicks});
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "DisputeEvents" ("Id", "DisputeId", "SequenceNumber", "EventType",
                "CaseStatus", "PublicDescription", "OccurredAt")
            VALUES ({eventId}, {id}, 1, 'DisputeSubmitted', 'Submitted', 'Legacy submission.', {SubmittedAt.UtcTicks});
            """);

        await migrator.MigrateAsync();
        Assert.Equal(1, await context.Disputes.Where(item => item.Id == id).Select(item => item.Version).SingleAsync());
        await migrator.MigrateAsync(previousMigration);
        await migrator.MigrateAsync();

        var stored = await new SqliteDisputeStore(context).ReadAsync(DemoCustomer, id, default);
        Assert.NotNull(stored);
        Assert.Equal("DSP-LEGACY", stored.Reference);
        Assert.Equal(1, stored.Version);
        Assert.Null(stored.ReversalOperationReference);
        Assert.Equal(eventId, Assert.Single(stored.Timeline).Id);
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
            context.Database.ExecuteSqlRawAsync("UPDATE \"DisputeEvents\" SET \"PublicDescription\" = 'Changed'"));
        Assert.Equal(31, await context.Transactions.CountAsync());
    }

    [Fact]
    public void Submit_IneligibleDecision_CannotCreateCase()
    {
        Assert.Throws<InvalidOperationException>(() => NewDispute(DisputeEligibilityOutcome.Ineligible));
        Assert.Throws<ArgumentException>(() => Dispute.Submit(Guid.NewGuid(), "DSP-TEST", DemoCustomer,
            Transaction(), "incorrectFee", "A sufficiently long description.",
            Eligibility(DisputeEligibilityOutcome.EligibleToSubmit), Guid.NewGuid()));
    }

    [Fact]
    public void Submit_ManualReviewDecision_RoutesCaseToManualReview()
    {
        var dispute = NewDispute(DisputeEligibilityOutcome.RequiresManualReview);

        Assert.Equal(DisputeCaseStatus.ManualReview, dispute.CaseStatus);
        Assert.Equal(DisputeCaseStatus.ManualReview, Assert.Single(dispute.Timeline).CaseStatus);
        Assert.Equal(DisputeFinancialStatus.NotInitiated, dispute.FinancialStatus);
    }

    private static async Task<TemporaryDatabase> SeededDatabase()
    {
        var database = new TemporaryDatabase();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        await SyntheticTransactionSeeder.SeedAsync(context, CancellationToken.None);
        return database;
    }

    private static Dispute NewDispute(DisputeEligibilityOutcome outcome) =>
        Dispute.Submit(Guid.NewGuid(), "DSP-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            DemoCustomer, Transaction(), "unrecognisedTransaction",
            "I did not make this purchase at the store.", Eligibility(outcome), Guid.NewGuid());

    private static DisputeTransaction Transaction() =>
        new(CardTransaction, TransactionCategory.CardPurchase, 750m, "ZAR", SubmittedAt.AddDays(-2),
            TransactionStatus.Posted, true, true, null, null, null, null);

    private static DisputeEligibilityResult Eligibility(DisputeEligibilityOutcome outcome) =>
        new(outcome, [], "Synthetic explanation.", "demo-v1", SubmittedAt);
}
