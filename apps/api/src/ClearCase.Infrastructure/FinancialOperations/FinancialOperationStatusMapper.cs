using ClearCase.Application.AgentDisputes;

namespace ClearCase.Infrastructure.Disputes;

public static class FinancialOperationStatusMapper
{
    public static FinancialOperationStatus Map(
        FinancialOperationStage stage, string? outcome, DateTimeOffset? leaseUntilAt, DateTimeOffset now) =>
        stage switch
        {
            FinancialOperationStage.Pending => FinancialOperationStatus.Pending,
            // A send whose lease expired without a recorded result may or may not have reached the provider.
            FinancialOperationStage.Dispatched => leaseUntilAt is { } until && until > now
                ? FinancialOperationStatus.InFlight : FinancialOperationStatus.Unknown,
            _ => outcome switch
            {
                "Confirmed" or "Succeeded" => FinancialOperationStatus.Succeeded,
                "Failed" => FinancialOperationStatus.Failed,
                _ => FinancialOperationStatus.Unknown
            }
        };
}
