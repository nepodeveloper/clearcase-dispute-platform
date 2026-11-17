using ClearCase.Application.AgentDisputes;
using ClearCase.Application.Disputes;
using Microsoft.Extensions.Logging;

namespace ClearCase.Infrastructure.Disputes;

/// <summary>
/// Compatibility facade: delegates scheduling and fencing to the durable store,
/// provider calls to strategies and case transitions to the outcome applier.
/// Neither stale claims nor unknown external results are automatically re-sent.
/// </summary>
public sealed class SqliteFinancialOperationCoordinator(
    IFinancialIntentStore intents,
    IRecordedFinancialOutcomeApplier applier,
    IDisputeStore disputes,
    IEnumerable<IFinancialOperationStrategy> strategies,
    ILogger<SqliteFinancialOperationCoordinator> logger) : IFinancialOperationCoordinator
{
    public async Task<DisputeView?> ExecuteAsync(
        FinancialOperationRequest request, CancellationToken cancellationToken)
    {
        var id = await ScheduleAsync(request, cancellationToken);
        if (id is null) return null;
        await ProcessAsync(id.Value, cancellationToken);
        return await disputes.ReadAsync(request.CustomerId, request.DisputeId, cancellationToken);
    }

    public Task<Guid?> ScheduleAsync(
        FinancialOperationRequest request, CancellationToken cancellationToken)
    {
        // Reject unavailable providers before committing an operation that cannot be sent.
        if (!ResolveStrategy(request.Kind).IsAvailable)
            throw new DisputeProcessingUnavailableException();
        return intents.ScheduleAsync(request, cancellationToken);
    }

    public async Task<bool> RecoverNextAsync(CancellationToken cancellationToken)
    {
        var next = await intents.FindRecoverableAsync(cancellationToken);
        if (next is null) return false;
        await ProcessAsync(next.Value, cancellationToken);
        return true;
    }

    private async Task ProcessAsync(Guid id, CancellationToken cancellationToken)
    {
        var claim = await intents.ClaimAsync(id, cancellationToken);
        if (claim is null) return;

        var outcome = claim.RecordedOutcome;
        if (claim.RequiresProviderExecution)
        {
            try
            {
                outcome = await ResolveStrategy(claim.Kind).ExecuteAsync(
                    new FinancialOperationExecution(claim.DisputeId, claim.Kind, claim.OperationReference),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Dispatch may have reached the provider. Recovery records Unknown, not a second send.
                throw;
            }
            catch (Exception error)
            {
                logger.LogError(error,
                    "Financial operation {OperationId} has unknown outcome; do not resend automatically.",
                    claim.Id);
                outcome = "Unknown";
            }

            if (!await intents.RecordOutcomeAsync(claim, outcome ?? "Unknown", cancellationToken))
                return; // A stale worker is not allowed to mutate case state.
        }

        if (!await intents.WasAppliedAsync(claim, cancellationToken))
            await applier.ApplyAsync(claim, outcome ?? "Unknown", cancellationToken);

        await intents.CompleteAsync(claim, cancellationToken);
    }

    private IFinancialOperationStrategy ResolveStrategy(FinancialOperationKind kind) =>
        strategies.SingleOrDefault(strategy => strategy.Supports(kind))
        ?? throw new DisputeProcessingUnavailableException();
}
