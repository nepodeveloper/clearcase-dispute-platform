using ClearCase.Application.AgentDisputes;
using ClearCase.Application.Disputes;
using Microsoft.Extensions.Logging;

namespace ClearCase.Infrastructure.Disputes;

/// <summary>
/// Stores immutable intent before invoking any financial adapter. Claims are committed before
/// adapter I/O. An interrupted in-flight send becomes Unknown on lease expiry, never reissued.
/// </summary>
public sealed class SqliteFinancialOperationCoordinator(
    FinancialOperationIntentStore intents,
    FinancialOperationResultApplier results,
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

    public async Task<Guid?> ScheduleAsync(
        FinancialOperationRequest request, CancellationToken cancellationToken)
    {
        var strategy = ResolveStrategy(request.Kind);
        if (!strategy.IsAvailable)
            throw new DisputeProcessingUnavailableException();

        return await intents.ScheduleAsync(request, strategy, cancellationToken);
    }

    public async Task<bool> RecoverNextAsync(CancellationToken cancellationToken)
    {
        var next = await intents.FindNextRecoverableAsync(cancellationToken);
        if (next is null) return false;
        await ProcessAsync(next.Value, cancellationToken);
        return true;
    }

    private async Task ProcessAsync(Guid id, CancellationToken cancellationToken)
    {
        var claim = await intents.ClaimAsync(id, cancellationToken);
        if (claim is null) return;

        var strategy = ResolveStrategy(claim.Intent.Kind);
        var outcome = claim.Outcome;
        if (claim.RequiresExecution)
        {
            try
            {
                outcome = await strategy.ExecuteAsync(
                    new FinancialOperationExecution(claim.Intent.DisputeId,
                        claim.Intent.Kind, claim.Intent.OperationReference),
                    cancellationToken);
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
                    claim.Intent.Id);
                outcome = "Unknown";
            }
            if (!await intents.RecordResultAsync(id, claim.LeaseId, outcome, cancellationToken))
                return;
        }

        await results.ApplyAsync(claim.Intent, strategy, outcome ?? "Unknown", cancellationToken);
        await intents.CompleteAsync(id, claim.LeaseId, cancellationToken);
    }

    private IFinancialOperationStrategy ResolveStrategy(FinancialOperationKind kind) =>
        strategies.SingleOrDefault(strategy => strategy.Supports(kind))
        ?? throw new DisputeProcessingUnavailableException();
}
