using ClearCase.Application.Disputes;
using ClearCase.Domain.Disputes;
using Microsoft.Extensions.Logging;

namespace ClearCase.Infrastructure.Disputes;

public sealed class ResilientDebitOrderProcessingGateway(
    IDebitOrderProcessingGateway inner,
    TimeProvider clock,
    ILogger<ResilientDebitOrderProcessingGateway> logger) : IDebitOrderProcessingGateway
{
    private const int FailureThreshold = 3;
    private static readonly TimeSpan CircuitOpenDuration = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromMilliseconds(100);
    private readonly object _sync = new();
    private int _consecutiveFailures;
    private DateTimeOffset _openUntil = DateTimeOffset.MinValue;

    public async Task<DebitOrderGatewayOutcome> ReverseAsync(
        string operationReference, CancellationToken cancellationToken)
    {
        if (!TryEnter())
            return DebitOrderGatewayOutcome.Unknown;
        try
        {
            var result = await inner.ReverseAsync(operationReference, cancellationToken);
            if (result == DebitOrderGatewayOutcome.Unknown)
                RecordFailure();
            else
                RecordSuccess();
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            RecordFailure();
            return DebitOrderGatewayOutcome.Unknown;
        }
    }

    public async Task<DebitOrderGatewayOutcome> GetReversalStatusAsync(
        string operationReference, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < FailureThreshold; attempt++)
        {
            if (!TryEnter())
                return DebitOrderGatewayOutcome.Unknown;
            try
            {
                var result = await inner.GetReversalStatusAsync(operationReference, cancellationToken);
                RecordSuccess();
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsTransient(exception))
            {
                RecordFailure();
                if (attempt + 1 == FailureThreshold || !TryEnter())
                    return DebitOrderGatewayOutcome.Unknown;
                var backoff = TimeSpan.FromMilliseconds(BaseRetryDelay.TotalMilliseconds * (1 << attempt));
                var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 101));
                await Task.Delay(backoff + jitter, clock, cancellationToken);
            }
        }

        return DebitOrderGatewayOutcome.Unknown;
    }

    private bool TryEnter()
    {
        lock (_sync)
        {
            if (_openUntil <= clock.GetUtcNow())
            {
                if (_openUntil != DateTimeOffset.MinValue)
                {
                    _openUntil = DateTimeOffset.MinValue;
                    _consecutiveFailures = 0;
                }
                return true;
            }
            return false;
        }
    }

    private void RecordSuccess()
    {
        lock (_sync)
        {
            _consecutiveFailures = 0;
            _openUntil = DateTimeOffset.MinValue;
        }
    }

    private void RecordFailure()
    {
        lock (_sync)
        {
            _consecutiveFailures++;
            if (_consecutiveFailures < FailureThreshold || _openUntil > clock.GetUtcNow())
                return;
            _openUntil = clock.GetUtcNow() + CircuitOpenDuration;
            logger.LogWarning("Debit-order gateway circuit opened until {CircuitOpenUntil} after {FailureCount} failures.",
                _openUntil, _consecutiveFailures);
        }
    }

    private static bool IsTransient(Exception exception) =>
        exception is TimeoutException or HttpRequestException or IOException;
}