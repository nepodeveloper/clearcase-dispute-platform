using ClearCase.Application.DisputeEligibility;
using ClearCase.Domain.Transactions;

namespace ClearCase.Infrastructure.Transactions;

/// <summary>Fixed synthetic collection snapshots, not a banking or mandate verification service.</summary>
public sealed class MockDebitOrderMandateReader : IDebitOrderMandateReader
{
    private static readonly Guid DemoCustomer = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly IReadOnlyDictionary<Guid, DebitOrderMandate> Records =
        new Dictionary<Guid, DebitOrderMandate>
        {
            [Guid.Parse("00000000-0000-0000-0000-000000000002")] =
                new(DebitOrderMandateType.EftDebitOrder, DebitOrderMandateStatus.Active, true, 420.50m, "ZAR", new(2026, 11, 9)),
            [Guid.Parse("00000000-0000-0000-0000-000000000005")] =
                new(DebitOrderMandateType.EftDebitOrder, DebitOrderMandateStatus.Active, true, 2000.01m, "ZAR", new(2026, 11, 6)),
            [Guid.Parse("00000000-0000-0000-0000-000000000011")] =
                new(DebitOrderMandateType.DebiCheck, DebitOrderMandateStatus.Active, true, 420.50m, "ZAR", new(2026, 10, 31)),
            [Guid.Parse("00000000-0000-0000-0000-000000000014")] =
                new(DebitOrderMandateType.RegisteredMandate, DebitOrderMandateStatus.Active, true, 420.50m, "ZAR", new(2026, 10, 28)),
            [Guid.Parse("00000000-0000-0000-0000-000000000017")] =
                new(DebitOrderMandateType.EftDebitOrder, DebitOrderMandateStatus.Active, false, 420.50m, "ZAR", new(2026, 10, 25)),
            [Guid.Parse("00000000-0000-0000-0000-000000000020")] =
                new(DebitOrderMandateType.EftDebitOrder, DebitOrderMandateStatus.Cancelled, true, 420.50m, "ZAR", new(2026, 10, 22)),
            [Guid.Parse("00000000-0000-0000-0000-000000000023")] =
                new(DebitOrderMandateType.DebiCheck, DebitOrderMandateStatus.Active, true, 400m, "ZAR", new(2026, 10, 19)),
            [Guid.Parse("00000000-0000-0000-0000-000000000026")] =
                new(DebitOrderMandateType.RegisteredMandate, DebitOrderMandateStatus.Suspended, true, 420.50m, "ZAR", new(2026, 10, 16)),
            [Guid.Parse("00000000-0000-0000-0000-000000000029")] =
                new(DebitOrderMandateType.EftDebitOrder, DebitOrderMandateStatus.Active, true, 420.50m, "ZAR", new(2026, 10, 13))
        };

    public Task<DebitOrderMandate?> ReadAsync(
        Guid customerId, Guid transactionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(customerId == DemoCustomer ? Records.GetValueOrDefault(transactionId) : null);
    }
}

/// <summary>No authoritative mandate adapter is configured outside the demo environments.</summary>
public sealed class UnavailableDebitOrderMandateReader : IDebitOrderMandateReader
{
    public Task<DebitOrderMandate?> ReadAsync(
        Guid customerId, Guid transactionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<DebitOrderMandate?>(null);
    }
}
