using ClearCase.Application.Disputes;

namespace ClearCase.Infrastructure.Disputes;

public sealed class FinancialOperationResultApplier(FinancialOperationIntentStore intents)
{
    public async Task ApplyAsync(
        FinancialOperationIntent intent, IFinancialOperationStrategy strategy, string outcome,
        CancellationToken cancellationToken)
    {
        if (await intents.EventWasAppliedAsync(
                intent, strategy.EventType(intent.Kind), cancellationToken))
            return;

        await strategy.ApplyResultAsync(intent, outcome, cancellationToken);
    }
}
