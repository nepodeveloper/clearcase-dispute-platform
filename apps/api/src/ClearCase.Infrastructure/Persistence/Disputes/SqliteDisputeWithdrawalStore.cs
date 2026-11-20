using ClearCase.Application.Disputes;
using ClearCase.Domain.Disputes;
using ClearCase.Infrastructure.Transactions;

namespace ClearCase.Infrastructure.Disputes;

public sealed class SqliteDisputeWithdrawalStore(
    TransactionDbContext context, IDisputeStore disputes) : IDisputeWithdrawalStore
{
    public Task<DisputeView?> WithdrawAsync(
        Guid customerId, WithdrawDisputeCommand command, DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        new DisputeTransitionPersistence(context, disputes).ExecuteAsync(
            customerId, command.DisputeId, command.ExpectedVersion, DisputeTransitionFamily.CustomerWithdrawal,
            "Withdrawn", occurredAt, command.CorrelationId, null,
            current => current.CanWithdraw
                ? Task.FromResult(new DisputeTransitionPlan(
                    DisputeCaseStatus.Closed, current.DecisionStatus, current.FinancialStatus,
                    DisputeWithdrawalRules.PublicDescription,
                    AddCategoryAudit: (database, row) =>
                    {
                        if (command.Reason is { } reason)
                            database.DisputeWithdrawals.Add(new PersistedDisputeWithdrawal
                            {
                                DisputeId = row.Id, Reason = reason, WithdrawnAt = occurredAt
                            });
                    }))
                : throw new DisputeWithdrawalNotAllowedException(),
            cancellationToken);
}
