using ClearCase.Application.AgentDisputes;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.Disputes;

public sealed record FinancialOperationClaim(
    FinancialOperationIntent Intent, Guid LeaseId, bool RequiresExecution, string? Outcome);

public sealed class FinancialOperationIntentStore(TransactionDbContext database, TimeProvider clock)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    public async Task<Guid?> ScheduleAsync(
        FinancialOperationRequest request, IFinancialOperationStrategy strategy,
        CancellationToken cancellationToken)
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

        strategy.Validate(new FinancialOperationDispute(
            item.Id, item.Category, item.ReasonCode, item.CaseStatus, item.DecisionStatus,
            item.FinancialStatus, item.Chargeback?.ToDomain()), request.Kind);
        var reference = strategy.OperationReference(item.Id, request.Kind);
        var existing = await database.FinancialOperationIntents.AsNoTracking()
            .SingleOrDefaultAsync(row => row.OperationReference == reference, cancellationToken);
        if (existing is not null)
            throw new DisputeConcurrencyConflictException();

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
        database.FinancialOperationIntents.Add(intent);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return intent.Id;
    }

    public async Task<Guid?> FindNextRecoverableAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        return await database.FinancialOperationIntents.AsNoTracking()
            .Where(item => item.Stage == FinancialOperationStage.Pending
                || ((item.Stage == FinancialOperationStage.Dispatched
                    || item.Stage == FinancialOperationStage.ResultRecorded)
                    && (item.LeaseUntilAt == null || item.LeaseUntilAt <= now)))
            .OrderBy(item => item.CreatedAt).ThenBy(item => item.Id)
            .Select(item => (Guid?)item.Id).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<FinancialOperationClaim?> ClaimAsync(Guid id, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var leaseId = Guid.NewGuid();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var current = await database.FinancialOperationIntents.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (current is null || current.Stage == FinancialOperationStage.Completed
            || (current.LeaseUntilAt is { } until && until > now))
            return null;

        var recoveredUnknown = current.Stage == FinancialOperationStage.Dispatched;
        var updated = await database.FinancialOperationIntents
            .Where(item => item.Id == id && item.Stage == current.Stage
                && (item.LeaseUntilAt == null || item.LeaseUntilAt <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Stage, recoveredUnknown
                    ? FinancialOperationStage.ResultRecorded
                    : current.Stage == FinancialOperationStage.Pending
                        ? FinancialOperationStage.Dispatched
                        : FinancialOperationStage.ResultRecorded)
                .SetProperty(item => item.Outcome, recoveredUnknown ? "Unknown" : current.Outcome)
                .SetProperty(item => item.LeaseId, leaseId)
                .SetProperty(item => item.LeaseUntilAt, now.Add(LeaseDuration))
                .SetProperty(item => item.Attempts, item => item.Attempts + 1), cancellationToken);
        if (updated != 1) throw new DisputeConcurrencyConflictException();
        await transaction.CommitAsync(cancellationToken);

        return new FinancialOperationClaim(
            ToIntent(current), leaseId,
            current.Stage == FinancialOperationStage.Pending,
            recoveredUnknown ? "Unknown" : current.Outcome);
    }

    public async Task<bool> RecordResultAsync(
        Guid id, Guid leaseId, string outcome, CancellationToken cancellationToken)
    {
        var updated = await database.FinancialOperationIntents
            .Where(item => item.Id == id && item.LeaseId == leaseId
                && item.Stage == FinancialOperationStage.Dispatched)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Outcome, outcome)
                .SetProperty(item => item.Stage, FinancialOperationStage.ResultRecorded),
                cancellationToken);
        return updated == 1;
    }

    public async Task<bool> CompleteAsync(
        Guid id, Guid leaseId, CancellationToken cancellationToken)
    {
        var updated = await database.FinancialOperationIntents
            .Where(item => item.Id == id && item.LeaseId == leaseId
                && item.Stage == FinancialOperationStage.ResultRecorded)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Stage, FinancialOperationStage.Completed)
                .SetProperty(item => item.LeaseId, (Guid?)null)
                .SetProperty(item => item.LeaseUntilAt, (DateTimeOffset?)null),
                cancellationToken);
        return updated == 1;
    }

    public Task<bool> EventWasAppliedAsync(
        FinancialOperationIntent intent, string eventType, CancellationToken cancellationToken) =>
        database.DisputeEvents.AsNoTracking().AnyAsync(item =>
            item.DisputeId == intent.DisputeId
            && item.SequenceNumber == intent.ExpectedVersion + 1
            && item.EventType == eventType, cancellationToken);

    private static FinancialOperationIntent ToIntent(PersistedFinancialOperation item) =>
        new(item.Id, item.DisputeId, item.CustomerId, item.Kind, item.OperationReference,
            item.ExpectedVersion, item.EmployeeId, item.CorrelationId);
}
