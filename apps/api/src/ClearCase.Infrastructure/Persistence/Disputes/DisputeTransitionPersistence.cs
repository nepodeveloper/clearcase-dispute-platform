using ClearCase.Application.AgentDisputes;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.Disputes;

/// <summary>Category-specific workflows supply state; this boundary owns all common writes.</summary>
internal sealed record DisputeTransitionPlan(
    DisputeCaseStatus CaseStatus,
    DisputeDecisionStatus DecisionStatus,
    DisputeFinancialStatus FinancialStatus,
    string Description,
    Action<PersistedDispute>? ApplyCategoryState = null,
    Action<TransactionDbContext, PersistedDispute>? AddCategoryAudit = null);

/// <summary>
/// One transactional writer for debit orders, bank fees and chargebacks.
/// A single SaveChanges commits state, case version, timeline, employee audit,
/// category audit and outbox intent. External adapters MUST run outside this writer.
/// </summary>
internal sealed class DisputeTransitionPersistence(
    TransactionDbContext context, IDisputeStore disputes)
{
    public async Task<DisputeView?> ExecuteAsync(
        Guid customerId, Guid disputeId, long? expectedVersion, DisputeTransitionFamily family,
        string command, DateTimeOffset occurredAt, string correlationId, Guid? employeeId,
        Func<DisputeView, Task<DisputeTransitionPlan>> transition, CancellationToken cancellationToken)
    {
        // SQLite obtains the writer lock before any view is read and before workflow execution.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var current = await disputes.ReadAsync(customerId, disputeId, cancellationToken);
        if (current is null) return null;
        if (expectedVersion is { } version && current.Version != version)
            throw new DisputeConcurrencyConflictException();

        var hasActiveEscalation = await context.DisputeEscalations.AsNoTracking().AnyAsync(
            item => item.DisputeId == disputeId
                && item.Status != DisputeEscalationStatus.Resolved, cancellationToken);
        if (!DisputeEscalationRules.CanProcess(hasActiveEscalation))
            throw new ActiveDisputeEscalationException();

        var plan = await transition(current);
        var aggregate = new DisputeLifecycle(current.Id, current.CaseStatus,
            current.DecisionStatus, current.FinancialStatus,
            current.Version, current.Timeline.Max(entry => entry.SequenceNumber));
        DisputeLifecycleTransition projected;
        try
        {
            projected = aggregate.Advance(family, command, plan.CaseStatus,
                plan.DecisionStatus, plan.FinancialStatus, plan.Description,
                occurredAt, Guid.NewGuid());
        }
        catch (InvalidDisputeLifecycleTransitionException)
        {
            throw new DisputeConcurrencyConflictException();
        }

        var row = await context.Disputes.Include(item => item.Chargeback)
            .SingleAsync(item => item.Id == disputeId && item.CustomerId == customerId,
                cancellationToken);
        row.CaseStatus = projected.Next.CaseStatus;
        row.DecisionStatus = projected.Next.DecisionStatus;
        row.FinancialStatus = projected.Next.FinancialStatus;
        row.Version = projected.Next.Version;

        // The plan can update category metadata and append category audit, but it
        // cannot bypass the domain-validated case state or version.
        plan.ApplyCategoryState?.Invoke(row);
        context.DisputeEvents.Add(new PersistedDisputeEvent
        {
            Id = projected.Event.Id, DisputeId = row.Id,
            SequenceNumber = projected.Event.SequenceNumber,
            EventType = projected.Event.EventType,
            CaseStatus = projected.Event.CaseStatus,
            PublicDescription = projected.Event.PublicDescription,
            OccurredAt = projected.Event.OccurredAt
        });
        context.OutboxMessages.Add(PersistedOutboxMessage.Transitioned(
            row, command, occurredAt, projected.EnvelopeEventType, correlationId));
        plan.AddCategoryAudit?.Invoke(context, row);
        if (employeeId is { } employee)
            context.DisputeCommandAuditEvents.Add(PersistedDisputeCommandAuditEvent.Create(
                row, employee, command, correlationId, occurredAt));

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DisputeConcurrencyConflictException();
        }

        var result = await disputes.ReadAsync(customerId, disputeId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
