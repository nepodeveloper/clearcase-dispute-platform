using System.Security.Cryptography;
using ClearCase.Application.AgentDisputes;
using ClearCase.Domain.DisputeEligibility;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;
using ClearCase.Infrastructure.Transactions;
using ClearCase.Infrastructure.SupervisorEscalations;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.AgentDisputes;

public sealed class SqliteAgentDisputeReader(TransactionDbContext context) : IAgentDisputeReader
{
    public async Task<AgentDisputePage> ReadPageAsync(
        ListAgentDisputesQuery query, CancellationToken cancellationToken)
    {
        var matches =
            from dispute in context.Disputes.AsNoTracking()
            join transaction in context.Transactions.AsNoTracking()
                on dispute.TransactionId equals transaction.Id
            select new { Dispute = dispute, Transaction = transaction };

        if (query.Search is { Length: > 0 } search)
        {
            matches = matches.Where(item =>
                item.Dispute.Reference.Contains(search)
                || item.Transaction.Description.Contains(search)
                || item.Dispute.ReasonCode.Contains(search));
        }

        if (query.Category is { } category)
            matches = matches.Where(item => item.Dispute.Category == category);
        if (query.CaseStatus is { } caseStatus)
            matches = matches.Where(item => item.Dispute.CaseStatus == caseStatus);
        if (query.After is { } cursor)
        {
            matches = matches.Where(item => item.Dispute.CreatedAt < cursor.CreatedAt
                || (item.Dispute.CreatedAt == cursor.CreatedAt
                    && string.Compare(item.Dispute.Reference, cursor.Reference) > 0));
        }

        var rows = await matches
            .OrderByDescending(item => item.Dispute.CreatedAt)
            .ThenBy(item => item.Dispute.Reference)
            .Select(item => new AgentDisputeListRow(
                item.Dispute.Id,
                item.Dispute.Reference,
                item.Dispute.CustomerId,
                item.Dispute.Category,
                item.Transaction.Description,
                item.Transaction.AmountMinorUnits,
                item.Transaction.Currency,
                item.Dispute.ReasonCode,
                item.Dispute.CaseStatus,
                item.Dispute.DecisionStatus,
                item.Dispute.FinancialStatus,
                item.Dispute.CreatedAt,
                context.DisputeEvents
                    .Where(entry => entry.DisputeId == item.Dispute.Id)
                    .Select(entry => (DateTimeOffset?)entry.OccurredAt)
                    .Max() ?? item.Dispute.CreatedAt,
                item.Dispute.Version))
            .Take(query.PageSize + 1)
            .ToArrayAsync(cancellationToken);

        var items = rows.Take(query.PageSize)
            .Select(row => new AgentDisputeListItem(
                row.Id, row.Reference, CustomerReference(row.CustomerId), row.Category,
                row.TransactionDescription, row.AmountMinorUnits, row.Currency, row.ReasonCode,
                row.CaseStatus, row.DecisionStatus, row.FinancialStatus, row.CreatedAt,
                row.LastActivityAt, row.Version))
            .ToArray();
        return new AgentDisputePage(items, rows.Length > query.PageSize);
    }

    public async Task<AgentDisputeDetails?> ReadAsync(
        Guid disputeId, CancellationToken cancellationToken)
    {
        var row = await (
            from dispute in context.Disputes.AsNoTracking()
            join transaction in context.Transactions.AsNoTracking()
                on dispute.TransactionId equals transaction.Id
            where dispute.Id == disputeId
            select new AgentDisputeDetailRow(
                dispute.Id,
                dispute.Reference,
                dispute.CustomerId,
                dispute.Category,
                dispute.ReasonCode,
                dispute.Description,
                dispute.EligibilityOutcome,
                dispute.EligibilityPolicyVersion,
                dispute.CaseStatus,
                dispute.DecisionStatus,
                dispute.FinancialStatus,
                dispute.CreatedAt,
                dispute.Version,
                transaction.Description,
                transaction.AmountMinorUnits,
                transaction.Currency,
                transaction.TransactionDate,
                transaction.MandateType,
                transaction.CollectionDate,
                transaction.MandateAuthorised,
                transaction.MandateMatchesCollection,
                dispute.ReversalOperationReference,
                dispute.BankChargeAdjustmentReference))
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
            return null;

        var timeline = await context.DisputeEvents.AsNoTracking()
            .Where(item => item.DisputeId == disputeId)
            .OrderBy(item => item.SequenceNumber)
            .Select(item => new AgentTimelineEvent(item.Id, item.SequenceNumber, item.EventType,
                item.CaseStatus, item.PublicDescription, item.OccurredAt))
            .ToArrayAsync(cancellationToken);

        var storedCardWorkflow = await context.Chargebacks.AsNoTracking()
            .Where(item => item.DisputeId == disputeId)
            .Select(item => new
            {
                item.Reference,
                item.ReasonCode,
                item.Status,
                item.LastOperationOutcome
            })
            .SingleOrDefaultAsync(cancellationToken);
        var cardWorkflow = storedCardWorkflow is null ? null : new AgentCardWorkflow(
            storedCardWorkflow.Reference, storedCardWorkflow.ReasonCode,
            storedCardWorkflow.Status.ToString(), storedCardWorkflow.LastOperationOutcome.ToString());
        var storedEscalation = await context.DisputeEscalations.AsNoTracking()
            .Where(item => item.DisputeId == disputeId
                && item.Status != DisputeEscalationStatus.Resolved)
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => new
            {
                item.Id,
                item.Reference,
                item.Reason,
                item.Status,
                item.CreatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);
        var activeEscalation = storedEscalation is null ? null : new AgentEscalationSummary(
            storedEscalation.Id, storedEscalation.Reference, storedEscalation.Reason,
            storedEscalation.Status.ToString(), storedEscalation.CreatedAt);

        return new AgentDisputeDetails(
            row.Id,
            row.Reference,
            CustomerReference(row.CustomerId),
            row.Category,
            row.ReasonCode,
            row.Description,
            row.EligibilityOutcome,
            row.EligibilityPolicyVersion,
            row.CaseStatus,
            row.DecisionStatus,
            row.FinancialStatus,
            row.CreatedAt,
            row.Version,
            new AgentTransactionDetails(
                row.TransactionDescription, row.AmountMinorUnits, row.Currency, row.TransactionDate,
                row.MandateType?.ToString(), row.CollectionDate, row.MandateAuthorised,
                row.MandateMatchesCollection),
            row.ReversalOperationReference,
            row.BankChargeAdjustmentReference,
            cardWorkflow,
            activeEscalation,
            timeline,
            []);
    }

    public Task<bool> ExistsAsync(Guid disputeId, CancellationToken cancellationToken) =>
        context.Disputes.AsNoTracking().AnyAsync(item => item.Id == disputeId, cancellationToken);

    internal static string CustomerReference(Guid customerId)
    {
        var digest = SHA256.HashData(customerId.ToByteArray());
        return $"CUS-{Convert.ToHexString(digest.AsSpan(0, 6))}";
    }

    private sealed record AgentDisputeListRow(
        Guid Id,
        string Reference,
        Guid CustomerId,
        TransactionCategory Category,
        string TransactionDescription,
        long AmountMinorUnits,
        string Currency,
        string ReasonCode,
        DisputeCaseStatus CaseStatus,
        DisputeDecisionStatus DecisionStatus,
        DisputeFinancialStatus FinancialStatus,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastActivityAt,
        long Version);

    private sealed record AgentDisputeDetailRow(
        Guid Id,
        string Reference,
        Guid CustomerId,
        TransactionCategory Category,
        string ReasonCode,
        string Description,
        DisputeEligibilityOutcome EligibilityOutcome,
        string EligibilityPolicyVersion,
        DisputeCaseStatus CaseStatus,
        DisputeDecisionStatus DecisionStatus,
        DisputeFinancialStatus FinancialStatus,
        DateTimeOffset CreatedAt,
        long Version,
        string TransactionDescription,
        long AmountMinorUnits,
        string Currency,
        DateTimeOffset TransactionDate,
        DebitOrderMandateType? MandateType,
        DateOnly? CollectionDate,
        bool? MandateAuthorised,
        bool? MandateMatchesCollection,
        string? ReversalOperationReference,
        string? BankChargeAdjustmentReference);
}
