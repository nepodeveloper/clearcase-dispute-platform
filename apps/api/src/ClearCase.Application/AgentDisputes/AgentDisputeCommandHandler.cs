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
    IAgentDisputeReader reader, IEnumerable<ICategoryCommandProcessor> processors)
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
        var processor = processors.SingleOrDefault(item => item.Category == target.Category)
            ?? throw new InvalidAgentDisputeCommandException();
        return await processor.HandleAsync(
            command, new EmployeeDisputeScope(target.CustomerId, command.EmployeeId), cancellationToken);
    }
}

public interface ICategoryCommandProcessor
{
    TransactionCategory Category { get; }

    Task<DisputeView?> HandleAsync(
        AgentDisputeCommand command, EmployeeDisputeScope employee, CancellationToken cancellationToken);
}

public sealed class DebitOrderCommandProcessor(IDebitOrderProcessingHandler handler) : ICategoryCommandProcessor
{
    public TransactionCategory Category => TransactionCategory.DebitOrder;

    public Task<DisputeView?> HandleAsync(
        AgentDisputeCommand command, EmployeeDisputeScope employee, CancellationToken cancellationToken) =>
        handler.HandleAsync(command.DisputeId, CommandParser.Parse<DebitOrderCommand>(command.Command),
            cancellationToken, command.ExpectedVersion, command.CorrelationId, employee);
}

public sealed class BankChargeCommandProcessor(IBankChargeProcessingHandler handler) : ICategoryCommandProcessor
{
    public TransactionCategory Category => TransactionCategory.BankCharge;

    public Task<DisputeView?> HandleAsync(
        AgentDisputeCommand command, EmployeeDisputeScope employee, CancellationToken cancellationToken) =>
        handler.HandleAsync(command.DisputeId, CommandParser.Parse<BankChargeCommand>(command.Command),
            cancellationToken, command.ExpectedVersion, command.CorrelationId, employee);
}

public sealed class ChargebackCommandProcessor(IChargebackProcessingHandler handler) : ICategoryCommandProcessor
{
    public TransactionCategory Category => TransactionCategory.CardPurchase;

    public Task<DisputeView?> HandleAsync(
        AgentDisputeCommand command, EmployeeDisputeScope employee, CancellationToken cancellationToken)
    {
        var parsed = CommandParser.Parse<ChargebackCommand>(command.Command);
        // Merchant-side events are simulated, so they are refused wherever simulations are disabled.
        if (!command.AllowSimulatedExternalUpdates
            && parsed is ChargebackCommand.MerchantAccept or ChargebackCommand.MerchantContest
                or ChargebackCommand.ReceiveRepresentment)
            throw new DisputeProcessingUnavailableException();
        return handler.HandleAsync(new(command.DisputeId, parsed, command.EvidenceId,
            command.EvidenceKind, command.CorrelationId, command.ExpectedVersion), cancellationToken, employee);
    }
}

internal static class CommandParser
{
    public static T Parse<T>(string? value) where T : struct, Enum =>
        Enum.TryParse<T>(value, out var command) && Enum.IsDefined(command) && command.ToString() == value
            ? command : throw new InvalidAgentDisputeCommandException();
}
