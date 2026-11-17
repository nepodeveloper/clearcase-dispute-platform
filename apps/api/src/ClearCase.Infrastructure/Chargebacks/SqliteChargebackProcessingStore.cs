using ClearCase.Application.AgentDisputes;
using ClearCase.Application.Chargebacks;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Chargebacks;
using ClearCase.Domain.Disputes;
using ClearCase.Infrastructure.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.Chargebacks;

public sealed class SqliteChargebackProcessingStore(TransactionDbContext context, IDisputeStore disputes)
    : IChargebackProcessingStore
{
    public Task<DisputeView?> ApplyAsync(
        Guid customerId, ChargebackProcessingCommand command, DateTimeOffset occurredAt,
        Func<DisputeView, Task<ChargebackTransition>> transition, CancellationToken cancellationToken,
        Guid? employeeId = null) =>
        new DisputeTransitionPersistence(context, disputes).ExecuteAsync(
            customerId, command.DisputeId, command.ExpectedVersion, DisputeTransitionFamily.Chargeback,
            command.Command.ToString(), occurredAt, command.CorrelationId, employeeId,
            async current =>
            {
                var next = await transition(current);
                return new DisputeTransitionPlan(next.CaseStatus, next.DecisionStatus,
                    next.FinancialStatus, next.Description,
                    row =>
                    {
                        row.Chargeback ??= new PersistedChargeback();
                        row.Chargeback.Update(next.Chargeback);
                    },
                    (db, row) => db.ChargebackAuditEvents.Add(new ChargebackAuditEvent
                    {
                        Id = Guid.NewGuid(), DisputeId = row.Id,
                        OperationReference = next.Chargeback.OperationReference(command.Command),
                        Command = command.Command, ChargebackStatus = next.Chargeback.Status,
                        DecisionStatus = row.DecisionStatus, FinancialStatus = row.FinancialStatus,
                        Outcome = next.Chargeback.LastOperationOutcome,
                        ActorId = employeeId ?? customerId, CorrelationId = command.CorrelationId,
                        EvidenceId = next.Chargeback.Evidence?.Id,
                        EvidenceKind = next.Chargeback.Evidence?.Kind, OccurredAt = occurredAt
                    }));
            }, cancellationToken);

}
