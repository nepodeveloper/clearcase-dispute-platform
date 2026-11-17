using ClearCase.Application.SupervisorEscalations;
using ClearCase.Domain.Disputes;
using ClearCase.Infrastructure.AgentDisputes;
using ClearCase.Infrastructure.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClearCase.Infrastructure.SupervisorEscalations;

public sealed class SqliteSupervisorEscalationStore(
    TransactionDbContext context,
    ILogger<SqliteSupervisorEscalationStore> logger)
    : ISupervisorEscalationStore
{
    private const int SqliteConstraintError = 19;

    public async Task<SupervisorEscalationPage> ReadPageAsync(
        ListSupervisorEscalationsQuery query, CancellationToken cancellationToken)
    {
        var matches =
            from escalation in context.DisputeEscalations.AsNoTracking()
            join dispute in context.Disputes.AsNoTracking() on escalation.DisputeId equals dispute.Id
            select new { Escalation = escalation, Dispute = dispute };

        if (query.Search is { Length: > 0 } search)
            matches = matches.Where(item => item.Escalation.Reference.Contains(search)
                || item.Dispute.Reference.Contains(search));
        if (query.Status is { } status)
            matches = matches.Where(item => item.Escalation.Status == status);
        if (query.After is { } cursor)
        {
            matches = matches.Where(item => item.Escalation.CreatedAt < cursor.CreatedAt
                || (item.Escalation.CreatedAt == cursor.CreatedAt
                    && string.Compare(item.Escalation.Reference, cursor.Reference) > 0));
        }

        var rows = await matches
            .OrderByDescending(item => item.Escalation.CreatedAt)
            .ThenBy(item => item.Escalation.Reference)
            .Select(item => new EscalationListRow(
                item.Escalation.Id,
                item.Escalation.Reference,
                item.Dispute.Id,
                item.Dispute.Reference,
                item.Dispute.CustomerId,
                item.Escalation.Reason,
                item.Escalation.Status,
                item.Escalation.CreatedAt))
            .Take(query.PageSize + 1)
            .ToArrayAsync(cancellationToken);

        return new SupervisorEscalationPage(
            rows.Take(query.PageSize).Select(item => new SupervisorEscalationItem(
                item.Id,
                item.Reference,
                item.DisputeId,
                item.DisputeReference,
                SqliteAgentDisputeReader.CustomerReference(item.CustomerId),
                item.Reason,
                item.Status,
                item.CreatedAt)).ToArray(),
            rows.Length > query.PageSize);
    }

    public Task<SupervisorEscalationDetails?> ReadAsync(
        Guid escalationId, CancellationToken cancellationToken) =>
        ReadDetailsAsync(escalationId, cancellationToken);

    public Task<ActiveDisputeEscalation?> ReadActiveForDisputeAsync(
        Guid disputeId, CancellationToken cancellationToken) =>
        context.DisputeEscalations.AsNoTracking()
            .Where(item => item.DisputeId == disputeId && item.Status != DisputeEscalationStatus.Resolved)
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => new ActiveDisputeEscalation(
                item.Id, item.Reference, item.Reason, item.Status, item.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<EscalationWriteResult> CreateAsync(
        CreateDisputeEscalationCommand command,
        string requestFingerprint,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var prior = await FindCreatedByIdempotencyAsync(
            command.DisputeId, command.EmployeeId, command.IdempotencyKey, cancellationToken);
        if (prior is not null)
            return await ReplayOrConflictAsync(prior, requestFingerprint, cancellationToken);

        var dispute = await context.Disputes.SingleOrDefaultAsync(
            item => item.Id == command.DisputeId, cancellationToken);
        if (dispute is null)
            return new(EscalationWriteOutcome.DisputeNotFound, null);
        if (!DisputeEscalationRules.CanCreate(
                dispute.CaseStatus, dispute.DecisionStatus, dispute.FinancialStatus)
            || !DisputeEscalationRules.IsValidReason(command.Reason))
        {
            return new(EscalationWriteOutcome.CaseNotEligible, null);
        }
        if (await context.DisputeEscalations.AnyAsync(
                item => item.DisputeId == command.DisputeId
                    && item.Status != DisputeEscalationStatus.Resolved,
                cancellationToken))
        {
            return new(EscalationWriteOutcome.ActiveEscalationExists, null);
        }

        var id = Guid.NewGuid();
        var escalation = new PersistedDisputeEscalation
        {
            Id = id,
            Reference = $"ESC-{id:N}".ToUpperInvariant(),
            DisputeId = dispute.Id,
            Reason = command.Reason,
            Status = DisputeEscalationStatus.Open,
            CreatedByEmployeeId = command.EmployeeId,
            CreatedAt = occurredAt,
            IdempotencyKey = command.IdempotencyKey,
            RequestFingerprint = requestFingerprint
        };
        escalation.History.Add(new PersistedDisputeEscalationAuditEvent
        {
            Id = Guid.NewGuid(),
            EscalationId = id,
            SequenceNumber = 1,
            Action = DisputeEscalationAction.Created,
            EmployeeId = command.EmployeeId,
            PrivateNote = command.Note,
            OccurredAt = occurredAt,
            CorrelationId = command.CorrelationId,
            IdempotencyKey = command.IdempotencyKey,
            RequestFingerprint = requestFingerprint
        });
        context.DisputeEscalations.Add(escalation);
        dispute.CaseStatus = DisputeCaseStatus.ManualReview;
        dispute.Version = checked(dispute.Version + 1);
        context.DisputeEvents.Add(new PersistedDisputeEvent
        {
            Id = Guid.NewGuid(),
            DisputeId = dispute.Id,
            SequenceNumber = await NextTimelineSequenceAsync(dispute.Id, cancellationToken),
            EventType = "SpecialistReview",
            CaseStatus = dispute.CaseStatus,
            PublicDescription = "Your case has been referred for additional review.",
            OccurredAt = occurredAt
        });
        context.OutboxMessages.Add(PersistedOutboxMessage.Transitioned(
            dispute, "SpecialistReview", occurredAt, "dispute.specialist-review.v1", command.CorrelationId));

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            var details = await ReadDetailsAsync(id, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(EscalationWriteOutcome.Created, details);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(exception,
                "Escalation creation conflicted on entity types {EntityTypes}.",
                string.Join(",", exception.Entries.Select(entry => entry.Metadata.ClrType.Name)));
            await transaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            return new(EscalationWriteOutcome.VersionConflict, null);
        }
        catch (DbUpdateException exception) when (
            IsUniqueConstraint(exception, "DisputeEscalations.DisputeId")
            || IsUniqueConstraint(exception,
                "DisputeEscalations.DisputeId, DisputeEscalations.CreatedByEmployeeId, DisputeEscalations.IdempotencyKey"))
        {
            await transaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            prior = await FindCreatedByIdempotencyAsync(
                command.DisputeId, command.EmployeeId, command.IdempotencyKey, cancellationToken);
            if (prior is not null)
                return await ReplayOrConflictAsync(prior, requestFingerprint, cancellationToken);
            return new(EscalationWriteOutcome.ActiveEscalationExists, null);
        }
    }

    public async Task<EscalationWriteResult> AcknowledgeAsync(
        AcknowledgeEscalationCommand command,
        string requestFingerprint,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var prior = await FindActionByIdempotencyAsync(
            command.EscalationId, command.EmployeeId, command.IdempotencyKey, cancellationToken);
        if (prior is not null)
            return await ReplayActionOrConflictAsync(prior, requestFingerprint, cancellationToken);

        var escalation = await context.DisputeEscalations
            .Include(item => item.History)
            .SingleOrDefaultAsync(item => item.Id == command.EscalationId, cancellationToken);
        if (escalation is null)
            return new(EscalationWriteOutcome.EscalationNotFound, null);
        if (escalation.CreatedByEmployeeId == command.EmployeeId)
            return new(EscalationWriteOutcome.ReviewerMustBeIndependent, null);
        if (!DisputeEscalationRules.CanAcknowledge(
                escalation.Status, escalation.CreatedByEmployeeId, command.EmployeeId))
        {
            return new(EscalationWriteOutcome.InvalidTransition, null);
        }

        escalation.Status = DisputeEscalationStatus.Acknowledged;
        AddAuditEvent(escalation, DisputeEscalationAction.Acknowledged, command.EmployeeId,
            null, command.IdempotencyKey, requestFingerprint, command.CorrelationId, occurredAt);
        return await SaveActionAsync(escalation, transaction, command.EmployeeId,
            command.IdempotencyKey, requestFingerprint, cancellationToken);
    }

    public async Task<EscalationWriteResult> ResolveAsync(
        ResolveEscalationCommand command,
        string requestFingerprint,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var prior = await FindActionByIdempotencyAsync(
            command.EscalationId, command.EmployeeId, command.IdempotencyKey, cancellationToken);
        if (prior is not null)
            return await ReplayActionOrConflictAsync(prior, requestFingerprint, cancellationToken);

        var escalation = await context.DisputeEscalations
            .Include(item => item.History)
            .SingleOrDefaultAsync(item => item.Id == command.EscalationId, cancellationToken);
        if (escalation is null)
            return new(EscalationWriteOutcome.EscalationNotFound, null);
        if (escalation.CreatedByEmployeeId == command.EmployeeId)
            return new(EscalationWriteOutcome.ReviewerMustBeIndependent, null);
        // Resolve only if the linked dispute still permits resuming an active review.
        // Both rows are loaded and written in this transaction; the dispute version and
        // escalation status are concurrency tokens protecting against stale updates.
        var dispute = await context.Disputes.SingleAsync(
            item => item.Id == escalation.DisputeId, cancellationToken);
        if (!DisputeEscalationRules.CanResolve(
                escalation.Status, escalation.CreatedByEmployeeId, command.EmployeeId,
                dispute.CaseStatus, dispute.DecisionStatus, dispute.FinancialStatus))
        {
            return new(EscalationWriteOutcome.InvalidTransition, null);
        }
        escalation.Status = DisputeEscalationStatus.Resolved;
        AddAuditEvent(escalation, DisputeEscalationAction.Resolved, command.EmployeeId,
            command.Note, command.IdempotencyKey, requestFingerprint, command.CorrelationId, occurredAt);
        dispute.CaseStatus = DisputeCaseStatus.UnderReview;
        dispute.Version = checked(dispute.Version + 1);
        context.DisputeEvents.Add(new PersistedDisputeEvent
        {
            Id = Guid.NewGuid(),
            DisputeId = dispute.Id,
            SequenceNumber = await NextTimelineSequenceAsync(dispute.Id, cancellationToken),
            EventType = "SpecialistReviewComplete",
            CaseStatus = dispute.CaseStatus,
            PublicDescription = "Additional review is complete and your case will continue to be investigated.",
            OccurredAt = occurredAt
        });
        context.OutboxMessages.Add(PersistedOutboxMessage.Transitioned(
            dispute, "SpecialistReviewComplete", occurredAt,
            "dispute.specialist-review-complete.v1", command.CorrelationId));
        return await SaveActionAsync(escalation, transaction, command.EmployeeId,
            command.IdempotencyKey, requestFingerprint, cancellationToken);
    }

    private async Task<EscalationWriteResult> SaveActionAsync(
        PersistedDisputeEscalation escalation,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        Guid employeeId,
        string idempotencyKey,
        string requestFingerprint,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            var details = await ReadDetailsAsync(escalation.Id, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(EscalationWriteOutcome.Applied, details);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(exception,
                "Escalation action conflicted on entity types {EntityTypes}.",
                string.Join(",", exception.Entries.Select(entry => entry.Metadata.ClrType.Name)));
            await transaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            return new(EscalationWriteOutcome.VersionConflict, null);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraint(
                   exception,
                   "DisputeEscalationAuditEvents.EscalationId, DisputeEscalationAuditEvents.EmployeeId, DisputeEscalationAuditEvents.IdempotencyKey"))
        {
            await transaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            var prior = await FindActionByIdempotencyAsync(
                escalation.Id, employeeId, idempotencyKey, cancellationToken);
            return prior is null
                ? new(EscalationWriteOutcome.VersionConflict, null)
                : await ReplayActionOrConflictAsync(prior, requestFingerprint, cancellationToken);
        }
    }

    private async Task<SupervisorEscalationDetails?> ReadDetailsAsync(
        Guid escalationId, CancellationToken cancellationToken)
    {
        var row = await (
            from escalation in context.DisputeEscalations.AsNoTracking()
            join dispute in context.Disputes.AsNoTracking() on escalation.DisputeId equals dispute.Id
            join transaction in context.Transactions.AsNoTracking() on dispute.TransactionId equals transaction.Id
            where escalation.Id == escalationId
            select new EscalationDetailsRow(
                escalation.Id,
                escalation.Reference,
                dispute.Id,
                dispute.Reference,
                dispute.CustomerId,
                dispute.Category,
                dispute.CaseStatus,
                dispute.DecisionStatus,
                dispute.FinancialStatus,
                escalation.Reason,
                escalation.Status,
                escalation.CreatedByEmployeeId,
                escalation.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
            return null;

        var history = await context.DisputeEscalationAuditEvents.AsNoTracking()
            .Where(item => item.EscalationId == escalationId)
            .OrderBy(item => item.SequenceNumber)
            .Select(item => new EscalationAuditEvent(
                item.Id, item.Action, item.EmployeeId, item.PrivateNote, item.OccurredAt, item.CorrelationId))
            .ToArrayAsync(cancellationToken);

        return new SupervisorEscalationDetails(
            row.Id,
            row.Reference,
            row.DisputeId,
            row.DisputeReference,
            SqliteAgentDisputeReader.CustomerReference(row.CustomerId),
            row.Category,
            row.CaseStatus,
            row.DecisionStatus,
            row.FinancialStatus,
            row.Reason,
            row.Status,
            row.CreatedByEmployeeId,
            row.CreatedAt,
            history);
    }

    private Task<PersistedDisputeEscalation?> FindCreatedByIdempotencyAsync(
        Guid disputeId, Guid employeeId, string idempotencyKey, CancellationToken cancellationToken) =>
        context.DisputeEscalations.AsNoTracking().SingleOrDefaultAsync(
            item => item.DisputeId == disputeId
                && item.CreatedByEmployeeId == employeeId
                && item.IdempotencyKey == idempotencyKey,
            cancellationToken);

    private async Task<EscalationWriteResult> ReplayOrConflictAsync(
        PersistedDisputeEscalation prior,
        string fingerprint,
        CancellationToken cancellationToken) =>
        prior.RequestFingerprint != fingerprint
            ? new(EscalationWriteOutcome.IdempotencyKeyReused, null)
            : new(EscalationWriteOutcome.Replayed,
                await ReadDetailsAsync(prior.Id, cancellationToken));

    private Task<PersistedDisputeEscalationAuditEvent?> FindActionByIdempotencyAsync(
        Guid escalationId, Guid employeeId, string idempotencyKey, CancellationToken cancellationToken) =>
        context.DisputeEscalationAuditEvents.AsNoTracking().SingleOrDefaultAsync(
            item => item.EscalationId == escalationId
                && item.EmployeeId == employeeId
                && item.IdempotencyKey == idempotencyKey,
            cancellationToken);

    private async Task<EscalationWriteResult> ReplayActionOrConflictAsync(
        PersistedDisputeEscalationAuditEvent prior,
        string fingerprint,
        CancellationToken cancellationToken) =>
        prior.RequestFingerprint != fingerprint
            ? new(EscalationWriteOutcome.IdempotencyKeyReused, null)
            : new(EscalationWriteOutcome.Replayed,
                await ReadDetailsAsync(prior.EscalationId, cancellationToken));

    private void AddAuditEvent(
        PersistedDisputeEscalation escalation,
        DisputeEscalationAction action,
        Guid employeeId,
        string? note,
        string idempotencyKey,
        string requestFingerprint,
        string correlationId,
        DateTimeOffset occurredAt)
    {
        var auditEvent = new PersistedDisputeEscalationAuditEvent
        {
            Id = Guid.NewGuid(),
            EscalationId = escalation.Id,
            SequenceNumber = escalation.History.Count + 1,
            Action = action,
            EmployeeId = employeeId,
            PrivateNote = note,
            OccurredAt = occurredAt,
            CorrelationId = correlationId,
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = requestFingerprint
        };
        escalation.History.Add(auditEvent);
        context.DisputeEscalationAuditEvents.Add(auditEvent);
    }

    private async Task<int> NextTimelineSequenceAsync(Guid disputeId, CancellationToken cancellationToken)
    {
        var maxSequence = await context.DisputeEvents.Where(item => item.DisputeId == disputeId)
            .Select(item => (int?)item.SequenceNumber)
            .MaxAsync(cancellationToken);
        return (maxSequence ?? 0) + 1;
    }

    private static bool IsUniqueConstraint(DbUpdateException exception, string columns) =>
        exception.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintError } sqlite
        && sqlite.Message.Contains(columns, StringComparison.Ordinal);

    private sealed record EscalationListRow(
        Guid Id,
        string Reference,
        Guid DisputeId,
        string DisputeReference,
        Guid CustomerId,
        string Reason,
        DisputeEscalationStatus Status,
        DateTimeOffset CreatedAt);

    private sealed record EscalationDetailsRow(
        Guid Id,
        string Reference,
        Guid DisputeId,
        string DisputeReference,
        Guid CustomerId,
        ClearCase.Domain.Transactions.TransactionCategory Category,
        DisputeCaseStatus CaseStatus,
        DisputeDecisionStatus DecisionStatus,
        DisputeFinancialStatus FinancialStatus,
        string Reason,
        DisputeEscalationStatus Status,
        Guid CreatedByEmployeeId,
        DateTimeOffset CreatedAt);
}
