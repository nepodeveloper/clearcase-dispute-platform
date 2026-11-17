using ClearCase.Application.AgentDisputes;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Chargebacks;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;
using ClearCase.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.Disputes;

public enum FinancialOperationStage { Pending, Dispatched, ResultRecorded, Completed }

public sealed class PersistedFinancialOperation
{
    public Guid Id { get; set; }
    public Guid DisputeId { get; set; }
    public Guid CustomerId { get; set; }
    public FinancialOperationKind Kind { get; set; }
    public string OperationReference { get; set; } = string.Empty;
    public long ExpectedVersion { get; set; }
    public Guid? EmployeeId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public FinancialOperationStage Stage { get; set; }
    public string? Outcome { get; set; }
    public int Attempts { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseUntilAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

internal static class FinancialOperationPersistenceModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PersistedFinancialOperation>();
        entity.ToTable("FinancialOperationIntents");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Kind).HasConversion<string>().IsRequired();
        entity.Property(item => item.Stage).HasConversion<string>().IsRequired();
        entity.Property(item => item.OperationReference).HasMaxLength(128).IsRequired();
        entity.Property(item => item.CorrelationId).HasMaxLength(128).IsRequired();
        entity.Property(item => item.CreatedAt).HasConversion(
            value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        entity.Property(item => item.LeaseUntilAt).HasConversion(
            value => value.HasValue ? value.Value.UtcTicks : (long?)null,
            value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : (DateTimeOffset?)null);
        entity.HasIndex(item => item.OperationReference).IsUnique();
        entity.HasIndex(item => new { item.Stage, item.LeaseUntilAt, item.CreatedAt });
        entity.HasOne<PersistedDispute>().WithMany().HasForeignKey(item => item.DisputeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}


/// <summary>Database transactions, eligibility transition guards and fenced claims.</summary>
public sealed class SqliteFinancialIntentStore(TransactionDbContext database, TimeProvider clock)
    : IFinancialIntentStore
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    public async Task<Guid?> ScheduleAsync(
        FinancialOperationRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var item = await database.Disputes.AsNoTracking().Include(row => row.Chargeback)
            .SingleOrDefaultAsync(row => row.Id == request.DisputeId
                && row.CustomerId == request.CustomerId, cancellationToken);
        if (item is null) return null;
        if (request.ExpectedVersion is { } version && version != item.Version)
            throw new DisputeConcurrencyConflictException();

        var hasEscalation = await database.DisputeEscalations.AnyAsync(row =>
            row.DisputeId == item.Id && row.Status != DisputeEscalationStatus.Resolved, cancellationToken);
        if (!DisputeEscalationRules.CanProcess(hasEscalation))
            throw new ActiveDisputeEscalationException();

        ValidateTransition(item, request.Kind);
        var reference = OperationReference(item.Id, request.Kind);
        var existing = await database.Set<PersistedFinancialOperation>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.OperationReference == reference, cancellationToken);
        if (existing is not null)
        {
            // The stable operation reference prevents a second external command, but
            // a new HTTP request is not an idempotency-key replay. Return 409 so
            // competing staff requests cannot both report a successful transition.
            throw new DisputeConcurrencyConflictException();
        }

        var intent = new PersistedFinancialOperation
        {
            Id = Guid.NewGuid(),
            DisputeId = item.Id,
            CustomerId = item.CustomerId,
            Kind = request.Kind,
            OperationReference = reference,
            ExpectedVersion = item.Version,
            EmployeeId = request.EmployeeId,
            CorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId)
                ? "local" : request.CorrelationId,
            Stage = FinancialOperationStage.Pending,
            CreatedAt = clock.GetUtcNow()
        };
        database.Set<PersistedFinancialOperation>().Add(intent);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return intent.Id;
    }


    public async Task<Guid?> FindRecoverableAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        return await database.Set<PersistedFinancialOperation>().AsNoTracking()
            .Where(item => item.Stage == FinancialOperationStage.Pending
                || ((item.Stage == FinancialOperationStage.Dispatched
                    || item.Stage == FinancialOperationStage.ResultRecorded)
                    && (item.LeaseUntilAt == null || item.LeaseUntilAt <= now)))
            .OrderBy(item => item.CreatedAt).ThenBy(item => item.Id)
            .Select(item => (Guid?)item.Id).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<FinancialIntentClaim?> ClaimAsync(Guid id, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        PersistedFinancialOperation? current;
        var leaseId = Guid.NewGuid();
        await using (var transaction = await database.Database.BeginTransactionAsync(cancellationToken))
        {
            current = await database.Set<PersistedFinancialOperation>().AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (current is null || current.Stage == FinancialOperationStage.Completed
                || (current.LeaseUntilAt is { } until && until > now))
                return null;

            var recoveredUnknown = current.Stage == FinancialOperationStage.Dispatched;
            var updated = await database.Set<PersistedFinancialOperation>()
                .Where(item => item.Id == id && item.Stage == current.Stage
                    && (item.LeaseUntilAt == null || item.LeaseUntilAt <= now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Stage, recoveredUnknown
                        ? FinancialOperationStage.ResultRecorded : current.Stage == FinancialOperationStage.Pending
                            ? FinancialOperationStage.Dispatched : FinancialOperationStage.ResultRecorded)
                    .SetProperty(item => item.Outcome, recoveredUnknown ? "Unknown" : current.Outcome)
                    .SetProperty(item => item.LeaseId, leaseId)
                    .SetProperty(item => item.LeaseUntilAt, now.Add(LeaseDuration))
                    .SetProperty(item => item.Attempts, item => item.Attempts + 1), cancellationToken);
            if (updated != 1) throw new DisputeConcurrencyConflictException();
            await transaction.CommitAsync(cancellationToken);
        }


        return new FinancialIntentClaim(
            current!.Id, current.DisputeId, current.CustomerId, current.Kind,
            current.OperationReference, current.ExpectedVersion, current.EmployeeId,
            current.CorrelationId, leaseId, current.Stage == FinancialOperationStage.Pending,
            current.Stage == FinancialOperationStage.Dispatched ? "Unknown" : current.Outcome);
    }

    public async Task<bool> RecordOutcomeAsync(
        FinancialIntentClaim claim, string outcome, CancellationToken cancellationToken)
    {
        var recorded = await database.Set<PersistedFinancialOperation>()
            .Where(item => item.Id == claim.Id && item.LeaseId == claim.LeaseId
                && item.Stage == FinancialOperationStage.Dispatched)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Outcome, outcome)
                .SetProperty(item => item.Stage, FinancialOperationStage.ResultRecorded),
                cancellationToken);
        return recorded == 1; // Lost lease must never apply stale financial results.
    }

    public Task<bool> WasAppliedAsync(FinancialIntentClaim claim, CancellationToken cancellationToken)
    {
        var eventType = EventType(claim.Kind);
        return database.DisputeEvents.AsNoTracking().AnyAsync(item =>
            item.DisputeId == claim.DisputeId && item.SequenceNumber == claim.ExpectedVersion + 1
                && item.EventType == eventType, cancellationToken);
    }

    public async Task CompleteAsync(FinancialIntentClaim claim, CancellationToken cancellationToken)
    {
        await database.Set<PersistedFinancialOperation>().Where(item =>
                item.Id == claim.Id && item.LeaseId == claim.LeaseId
                && item.Stage == FinancialOperationStage.ResultRecorded)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Stage, FinancialOperationStage.Completed)
                .SetProperty(item => item.LeaseId, (Guid?)null)
                .SetProperty(item => item.LeaseUntilAt, (DateTimeOffset?)null), cancellationToken);
    }

    private static void ValidateTransition(PersistedDispute dispute, FinancialOperationKind kind)
    {
        switch (kind)
        {
            case FinancialOperationKind.DebitReversal
                when dispute.Category == TransactionCategory.DebitOrder:
                new DebitOrderState(dispute.CaseStatus, dispute.DecisionStatus, dispute.FinancialStatus)
                    .Apply(DebitOrderCommand.ExecuteReversal, true, false);
                return;
            case FinancialOperationKind.BankChargeAdjustment
                when dispute.Category == TransactionCategory.BankCharge:
                new BankChargeState(dispute.CaseStatus, dispute.DecisionStatus, dispute.FinancialStatus)
                    .Apply(BankChargeCommand.ExecuteAdjustment, true);
                return;
            case FinancialOperationKind.CardNetworkInitiate or
                 FinancialOperationKind.CardNetworkForwardToAcquirer or
                 FinancialOperationKind.CardNetworkSettlement
                when dispute.Category == TransactionCategory.CardPurchase
                    && dispute.CaseStatus != DisputeCaseStatus.Closed:
                var command = CardCommand(kind);
                var card = dispute.Chargeback?.ToDomain()
                    ?? Chargeback.Create(dispute.Id, dispute.ReasonCode);
                card.Validate(command, dispute.DecisionStatus, dispute.FinancialStatus, null);
                return;
            default:
                throw kind switch
                {
                    FinancialOperationKind.DebitReversal => new InvalidDebitOrderTransitionException(),
                    FinancialOperationKind.BankChargeAdjustment => new InvalidBankChargeTransitionException(),
                    _ => new InvalidChargebackTransitionException()
                };
        }
    }

    private static string OperationReference(Guid id, FinancialOperationKind kind) => kind switch
    {
        FinancialOperationKind.DebitReversal => $"MOCK-DO-{id:N}",
        FinancialOperationKind.BankChargeAdjustment => $"MOCK-FEE-{id:N}",
        _ => $"MOCK-CARD-{id:N}-{CardCommand(kind)}"
    };

    private static ChargebackCommand CardCommand(FinancialOperationKind kind) => kind switch
    {
        FinancialOperationKind.CardNetworkInitiate => ChargebackCommand.Initiate,
        FinancialOperationKind.CardNetworkForwardToAcquirer => ChargebackCommand.ForwardToAcquirer,
        FinancialOperationKind.CardNetworkSettlement => ChargebackCommand.ExecuteSettlement,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string EventType(FinancialOperationKind kind) => kind switch
    {
        FinancialOperationKind.DebitReversal => "DebitOrderExecuteReversal",
        FinancialOperationKind.BankChargeAdjustment => "BankChargeExecuteAdjustment",
        _ => $"Chargeback{CardCommand(kind)}"
    };
}
