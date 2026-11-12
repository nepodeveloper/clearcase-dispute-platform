using System.Globalization;
using ClearCase.Application.Transactions;
using ClearCase.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.Transactions;

public static class SyntheticTransactionSeeder
{
    private static readonly Guid DemoCustomer = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherCustomer = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly string[] Descriptions = ["Woolworths", "Monthly insurance", "Account service fee"];
    private static readonly long[] AmountsMinorUnits = [75000, 42050, 1500];
    private static readonly DateTimeOffset Start = new(2026, 11, 10, 8, 30, 0, TimeSpan.Zero);

    public static async Task SeedAsync(TransactionDbContext context, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        foreach (var item in CreateTransactions())
        {
            var type = item.Type.ToString();
            var status = item.Status.ToString();
            var dateTicks = item.TransactionDate.UtcTicks;
            var collectionDate = item.CollectionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var mandateType = item.MandateType?.ToString();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Transactions"
                ("Id", "CustomerId", "Type", "Description", "MerchantName", "AmountMinorUnits",
                 "Currency", "TransactionDate", "Status", "Reference", "IsDisputable", "IsDebit",
                 "HasRequiredDisputeInformation", "CollectionDate", "MandateType",
                 "MandateAuthorised", "MandateMatchesCollection")
                VALUES ({item.Id}, {item.CustomerId}, {type}, {item.Description}, {item.MerchantName},
                 {item.AmountMinorUnits}, {item.Currency}, {dateTicks}, {status}, {item.Reference},
                 {item.IsDisputable}, {item.IsDebit}, {item.HasRequiredDisputeInformation},
                 {collectionDate}, {mandateType}, {item.MandateAuthorised}, {item.MandateMatchesCollection})
                ON CONFLICT("Id") DO NOTHING
                """, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private static IEnumerable<PersistedTransaction> CreateTransactions()
    {
        for (var index = 1; index <= 30; index++)
        {
            var typeIndex = (index - 1) % 3;
            var date = Start.AddDays(-Math.Min(index - 1, 28));
            var type = (TransactionType)typeIndex;
            var isDebitOrder = type == TransactionType.DebitOrder;
            yield return new PersistedTransaction
            {
                Id = Guid.Parse($"00000000-0000-0000-0000-{index:000000000000}"),
                CustomerId = DemoCustomer,
                Type = type,
                Description = Descriptions[typeIndex],
                MerchantName = typeIndex == 2 ? null : Descriptions[typeIndex],
                AmountMinorUnits = index == 5 ? 200001 : AmountsMinorUnits[typeIndex],
                Currency = "ZAR",
                TransactionDate = date,
                Status = index == 4 ? TransactionStatus.PendingAuthorization : TransactionStatus.Posted,
                Reference = $"TXN-2026-{index:000000}",
                IsDisputable = true,
                IsDebit = true,
                HasRequiredDisputeInformation = index != 8,
                CollectionDate = isDebitOrder ? DateOnly.FromDateTime(date.UtcDateTime) : null,
                MandateType = isDebitOrder
                    ? index == 11 ? DebitOrderMandateType.DebiCheck
                        : index == 8 ? null : DebitOrderMandateType.EftDebitOrder
                    : null,
                MandateAuthorised = isDebitOrder ? index == 8 ? null : true : null,
                MandateMatchesCollection = isDebitOrder ? index == 8 ? null : true : null
            };
        }

        yield return new PersistedTransaction
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000099"),
            CustomerId = OtherCustomer,
            Type = TransactionType.CardPurchase,
            Description = "Other synthetic customer",
            MerchantName = "Other synthetic customer",
            AmountMinorUnits = 2500,
            Currency = "ZAR",
            TransactionDate = Start,
            Status = TransactionStatus.Posted,
            Reference = "TXN-2026-000099",
            IsDisputable = true,
            IsDebit = true,
            HasRequiredDisputeInformation = true
        };
    }
}
