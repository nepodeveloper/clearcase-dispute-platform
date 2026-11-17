using ClearCase.Application.AgentDisputes;
using ClearCase.Application.Chargebacks;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Chargebacks;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;
using ClearCase.Infrastructure.Chargebacks;
using ClearCase.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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

/// <summary>
/// Stores immutable intent before invoking any financial adapter. Claims are committed before
/// adapter I/O. An interrupted in-flight send becomes Unknown on lease expiry, never reissued.
/// </summary>
public sealed class SqliteFinancialOperationCoordinator(
    TransactionDbContext database,
    IDisputeStore disputes,
    IDebitOrderProcessingStore debitOrders,
    IBankChargeProcessingStore bankCharges,
    IChargebackProcessingStore chargebacks,
    IDebitOrderProcessingGateway debitGateway,
    IBankChargeAdjustmentGateway bankGateway,
    ICardNetworkGateway cardGateway,
    TimeProvider clock,
    ILogger<SqliteFinancialOperationCoordinator> logger) : IFinancialOperationCoordinator
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    public async Task<DisputeView?> ExecuteAsync(
        FinancialOperationRequest request, CancellationToken cancellationToken)
    {
        var id = await ScheduleAsync(request, cancellationToken);
        if (id is null) return null;
        await ProcessAsync(id.Value, cancellationToken);
        return await disputes.ReadAsync(request.CustomerId, request.DisputeId, cancellationToken);
    }

    public async Task<Guid?> ScheduleAsync(
        FinancialOperationRequest request, CancellationToken cancellationToken)
    {
        // No production adapter has been integrated. A missing adapter is 503, not an
        // operation that pretends to be submitted or financially confirmed.
        if (request.Kind == FinancialOperationKind.DebitReversal
                && debitGateway is UnavailableDisputeProcessingGateway
            || request.Kind == FinancialOperationKind.BankChargeAdjustment
                && bankGateway is UnavailableDisputeProcessingGateway
            || (request.Kind is FinancialOperationKind.CardNetworkInitiate
                or FinancialOperationKind.CardNetworkForwardToAcquirer
                or FinancialOperationKind.CardNetworkSettlement)
                && cardGateway is UnavailableDisputeProcessingGateway)
            throw new DisputeProcessingUnavailableException();

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

    public async Task<bool> RecoverNextAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var next = await database.Set<PersistedFinancialOperation>().AsNoTracking()
            .Where(item => item.Stage == FinancialOperationStage.Pending
                || ((item.Stage == FinancialOperationStage.Dispatched
                    || item.Stage == FinancialOperationStage.ResultRecorded)
                    && (item.LeaseUntilAt == null || item.LeaseUntilAt <= now)))
            .OrderBy(item => item.CreatedAt).ThenBy(item => item.Id)
            .Select(item => (Guid?)item.Id).FirstOrDefaultAsync(cancellationToken);
        if (next is null) return false;
        await ProcessAsync(next.Value, cancellationToken);
        return true;
    }

    private async Task ProcessAsync(Guid id, CancellationToken cancellationToken)
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
                return;

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

        var outcome = current.Stage == FinancialOperationStage.Dispatched
            ? "Unknown" : current.Outcome;
        if (current.Stage == FinancialOperationStage.Pending)
        {
            try
            {
                // No database transaction is active during the external call.
                outcome = await ExecuteAdapterAsync(current, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Unknown if request reached provider; recovery must not submit again.
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception,
                    "Financial operation {OperationId} has unknown outcome; do not re-submit automatically.",
                    current.Id);
                outcome = "Unknown";
            }
            var recorded = await database.Set<PersistedFinancialOperation>()
                .Where(item => item.Id == id && item.LeaseId == leaseId
                    && item.Stage == FinancialOperationStage.Dispatched)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Outcome, outcome)
                    .SetProperty(item => item.Stage, FinancialOperationStage.ResultRecorded),
                    cancellationToken);
            if (recorded != 1) return; // Lease was lost. Never apply a stale result.
        }

        // The result is durable before we mutate the case. Recovery replays this *result*
        // without issuing a second external command, even after a process restart.
        var eventType = EventType(current.Kind);
        var alreadyApplied = await database.DisputeEvents.AsNoTracking().AnyAsync(item =>
            item.DisputeId == current.DisputeId && item.SequenceNumber == current.ExpectedVersion + 1
                && item.EventType == eventType, cancellationToken);
        if (!alreadyApplied)
            await ApplyRecordedResultAsync(current, outcome ?? "Unknown", cancellationToken);

        await database.Set<PersistedFinancialOperation>().Where(item =>
                item.Id == id && item.LeaseId == leaseId
                && item.Stage == FinancialOperationStage.ResultRecorded)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Stage, FinancialOperationStage.Completed)
                .SetProperty(item => item.LeaseId, (Guid?)null)
                .SetProperty(item => item.LeaseUntilAt, (DateTimeOffset?)null), cancellationToken);
    }

    private async Task<string> ExecuteAdapterAsync(
        PersistedFinancialOperation intent, CancellationToken cancellationToken) =>
        intent.Kind switch
        {
            FinancialOperationKind.DebitReversal =>
                (await debitGateway.ReverseAsync(intent.OperationReference, cancellationToken)).ToString(),
            FinancialOperationKind.BankChargeAdjustment =>
                (await bankGateway.AdjustAsync(intent.OperationReference, cancellationToken)).ToString(),
            FinancialOperationKind.CardNetworkInitiate or
                FinancialOperationKind.CardNetworkForwardToAcquirer or
                FinancialOperationKind.CardNetworkSettlement =>
                (await cardGateway.ExecuteAsync(
                    new CardNetworkRequest(intent.OperationReference,
                        $"MOCK-CB-{intent.DisputeId:N}", CardCommand(intent.Kind)),
                    cancellationToken)).ToString(),
            _ => throw new InvalidOperationException("Unrecognised financial operation kind.")
        };

    private async Task ApplyRecordedResultAsync(
        PersistedFinancialOperation intent, string outcome, CancellationToken cancellationToken)
    {
        switch (intent.Kind)
        {
            case FinancialOperationKind.DebitReversal:
                var debitOutcome = Enum.Parse<DebitOrderGatewayOutcome>(outcome switch
                {
                    "Unknown" => nameof(DebitOrderGatewayOutcome.Unknown),
                    _ => outcome
                });
                await debitOrders.ApplyAsync(intent.CustomerId, intent.DisputeId,
                    DebitOrderCommand.ExecuteReversal, dispute =>
                    {
                        var state = new DebitOrderState(
                            dispute.CaseStatus, dispute.DecisionStatus, dispute.FinancialStatus);
                        return Task.FromResult(state.CompleteReversal(debitOutcome));
                    }, clock.GetUtcNow(), cancellationToken, intent.ExpectedVersion,
                    intent.CorrelationId, intent.EmployeeId);
                break;
            case FinancialOperationKind.BankChargeAdjustment:
                var bankOutcome = Enum.Parse<BankChargeAdjustmentOutcome>(outcome);
                await bankCharges.ApplyAsync(intent.CustomerId, intent.DisputeId,
                    BankChargeCommand.ExecuteAdjustment, clock.GetUtcNow(), dispute =>
                    {
                        var state = new BankChargeState(
                            dispute.CaseStatus, dispute.DecisionStatus, dispute.FinancialStatus);
                        return Task.FromResult(state.CompleteAdjustment(bankOutcome));
                    }, cancellationToken, intent.ExpectedVersion, intent.CorrelationId, intent.EmployeeId);
                break;
            default:
                var command = CardCommand(intent.Kind);
                var cardOutcome = Enum.Parse<CardNetworkOutcome>(outcome switch
                {
                    "Unknown" => nameof(CardNetworkOutcome.OutcomeUnknown),
                    _ => outcome
                });
                await chargebacks.ApplyAsync(intent.CustomerId,
                    new ChargebackProcessingCommand(intent.DisputeId, command, null, null,
                        intent.CorrelationId, intent.ExpectedVersion),
                    clock.GetUtcNow(), dispute =>
                    {
                        var current = dispute.Chargeback
                            ?? Chargeback.Create(dispute.Id, dispute.ReasonCode);
                        var result = current.Apply(command, dispute.DecisionStatus,
                            dispute.FinancialStatus, null, cardOutcome);
                        return Task.FromResult(result);
                    }, cancellationToken, intent.EmployeeId);
                break;
        }
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
