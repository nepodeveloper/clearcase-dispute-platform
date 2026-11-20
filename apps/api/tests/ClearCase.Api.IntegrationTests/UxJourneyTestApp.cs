using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClearCase.Domain.DisputeEligibility;
using ClearCase.Application.Transactions;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;
using ClearCase.Infrastructure.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClearCase.Api.IntegrationTests;

internal sealed class UxClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class UxApp
{
    public const string DemoCustomerId = "11111111-1111-1111-1111-111111111111";
    public const string OtherCustomerId = "22222222-2222-2222-2222-222222222222";
    public static readonly DateTimeOffset Start = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _host;

    public UxApp(
        SqliteWebApplicationFactory factory, string environment = "Testing",
        IDictionary<string, string?>? settings = null)
    {
        Clock = new UxClock(Start);
        _host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            if (settings is not null)
                builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(settings));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
            });
        });
    }

    public UxClock Clock { get; }

    public async Task<HttpClient> SignInAsync(string username)
    {
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { username, password = TestIdentityPasswords.For(username) });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var me = await client.GetAsync("/api/v1/auth/me");
        using var body = await ReadJsonAsync(me);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", body.RootElement.GetProperty("csrfToken").GetString());
        return client;
    }

    public HttpClient Anonymous() => _host.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost")
    });

    public async Task<T> WithDatabaseAsync<T>(Func<TransactionDbContext, Task<T>> action)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TransactionDbContext>());
    }

    public Task WithDatabaseAsync(Func<TransactionDbContext, Task> action) =>
        WithDatabaseAsync(async database =>
        {
            await action(database);
            return 0;
        });

    public Task<Guid> UserIdAsync(string username) =>
        WithDatabaseAsync(database => database.Users.Where(user => user.UserName == username)
            .Select(user => user.Id).SingleAsync());

    public async Task<Guid> SeedDisputeAsync(
        string customerId = DemoCustomerId, TransactionType type = TransactionType.BankCharge,
        DisputeCaseStatus status = DisputeCaseStatus.Submitted,
        DisputeDecisionStatus decision = DisputeDecisionStatus.Pending,
        DisputeFinancialStatus financial = DisputeFinancialStatus.NotInitiated,
        DateTimeOffset? createdAt = null, string? reference = null)
    {
        var transactionId = Guid.NewGuid();
        var disputeId = Guid.NewGuid();
        var created = createdAt ?? Start;
        await WithDatabaseAsync(async database =>
        {
            database.Transactions.Add(new PersistedTransaction
            {
                Id = transactionId, CustomerId = Guid.Parse(customerId), Type = type,
                Description = "Synthetic UX journey transaction", AmountMinorUnits = 12345, Currency = "ZAR",
                TransactionDate = created.AddDays(-1), Status = TransactionStatus.Posted, IsDisputable = true,
                IsDebit = true, HasRequiredDisputeInformation = true
            });
            database.Disputes.Add(new PersistedDispute
            {
                Id = disputeId, Reference = reference ?? $"DSP-UX-{Guid.NewGuid():N}"[..20],
                CustomerId = Guid.Parse(customerId), TransactionId = transactionId,
                Category = (TransactionCategory)type,
                ReasonCode = type == TransactionType.BankCharge ? "incorrectFee" : "unrecognisedTransaction",
                Description = "The customer statement contains no real personal data.",
                CaseStatus = status, DecisionStatus = decision, FinancialStatus = financial,
                EligibilityOutcome = DisputeEligibilityOutcome.EligibleToSubmit,
                EligibilityPolicyVersion = "synthetic-test-v1", Version = 1, CreatedAt = created,
                Events =
                [
                    new PersistedDisputeEvent
                    {
                        Id = Guid.NewGuid(), DisputeId = disputeId, SequenceNumber = 1,
                        EventType = "DisputeSubmitted", CaseStatus = status,
                        PublicDescription = "Your dispute was received and is awaiting review.", OccurredAt = created
                    }
                ]
            });
            await database.SaveChangesAsync();
        });
        return disputeId;
    }

    public static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    public static string Code(JsonDocument body) => body.RootElement.GetProperty("code").GetString()!;
}
