using ClearCase.Application.Chargebacks;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Chargebacks;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;

namespace ClearCase.Application.AgentDisputes;

public sealed record AgentDisputeProcessingTarget(
    Guid CustomerId, TransactionCategory Category, bool HasActiveEscalation);

public sealed record EmployeeDisputeScope(Guid CustomerId, Guid EmployeeId);

public sealed record AgentDisputeCommand(
    Guid DisputeId, Guid EmployeeId, string? Command, long ExpectedVersion,
    Guid? EvidenceId, ChargebackEvidenceKind? EvidenceKind, string CorrelationId,
    bool AllowSimulatedExternalUpdates);

public sealed class InvalidAgentDisputeCommandException : Exception { }
public sealed class ActiveDisputeEscalationException : Exception { }
public sealed class DisputeProcessingUnavailableException : Exception { }

public interface IAgentDisputeCommandHandler
{
    Task<DisputeView?> HandleAsync(AgentDisputeCommand command, CancellationToken cancellationToken);
}

public sealed class AgentDisputeCommandHandler(
    IAgentDisputeReader reader, IDebitOrderProcessingHandler debitOrders,
    IBankChargeProcessingHandler bankCharges, IChargebackProcessingHandler chargebacks)
    : IAgentDisputeCommandHandler
{
    public async Task<DisputeView?> HandleAsync(AgentDisputeCommand command, CancellationToken cancellationToken)
    {
        if (command.EmployeeId == Guid.Empty || command.ExpectedVersion <= 0)
            throw new ArgumentException("A trusted employee and positive expected version are required.", nameof(command));
        var target = await reader.ReadProcessingTargetAsync(command.DisputeId, cancellationToken);
        if (target is null) return null;
        // Escalation writes increment the version, so the processing version guard also protects races.
        if (!DisputeEscalationRules.CanProcess(target.HasActiveEscalation))
            throw new ActiveDisputeEscalationException();
        if (target.Category == TransactionCategory.CardPurchase && !command.AllowSimulatedExternalUpdates
            && command.Command is nameof(ChargebackCommand.MerchantAccept) or nameof(ChargebackCommand.MerchantContest)
                or nameof(ChargebackCommand.ReceiveRepresentment))
            throw new DisputeProcessingUnavailableException();
        var employee = new EmployeeDisputeScope(target.CustomerId, command.EmployeeId);
        return target.Category switch
        {
            TransactionCategory.DebitOrder => await debitOrders.HandleAsync(
                command.DisputeId, Parse<DebitOrderCommand>(command.Command), cancellationToken,
                command.ExpectedVersion, command.CorrelationId, employee),
            TransactionCategory.BankCharge => await bankCharges.HandleAsync(
                command.DisputeId, Parse<BankChargeCommand>(command.Command), cancellationToken,
                command.ExpectedVersion, command.CorrelationId, employee),
            TransactionCategory.CardPurchase => await chargebacks.HandleAsync(new(
                command.DisputeId, Parse<ChargebackCommand>(command.Command), command.EvidenceId,
                command.EvidenceKind, command.CorrelationId, command.ExpectedVersion), cancellationToken, employee),
            _ => throw new InvalidAgentDisputeCommandException()
        };
    }

    private static T Parse<T>(string? value) where T : struct, Enum =>
        Enum.TryParse<T>(value, out var command) && Enum.IsDefined(command) && command.ToString() == value
            ? command : throw new InvalidAgentDisputeCommandException();
}
