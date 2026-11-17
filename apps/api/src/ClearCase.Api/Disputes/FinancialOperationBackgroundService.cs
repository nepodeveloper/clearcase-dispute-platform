using ClearCase.Application.Disputes;

namespace ClearCase.Api.Disputes;

/// <summary>
/// Opt-in worker. A crashed or cancelled external send is reconciled as Unknown,
/// not automatically submitted again. Enable only with an explicitly configured adapter.
/// </summary>
internal sealed class FinancialOperationBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<FinancialOperationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var coordinator = scope.ServiceProvider.GetRequiredService<IFinancialOperationCoordinator>();
                if (await coordinator.RecoverNextAsync(stoppingToken))
                    continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception,
                    "Financial operation recovery failed; no external financial command will be blindly retried.");
            }
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}
