using System.Net;
using System.Net.Http.Json;
using ClearCase.Application.AgentDisputes;
using ClearCase.Domain.DisputeEligibility;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ClearCase.Api.IntegrationTests;

public sealed class StaffActionCatalogTests
{
    [Fact]
    public void UnknownDebitReversalOffersOnlyReconciliation()
    {
        var actions = StaffActionCatalog.For(Details(TransactionCategory.DebitOrder,
            DisputeCaseStatus.ReversalRequested, DisputeDecisionStatus.Approved,
            DisputeFinancialStatus.OutcomeUnknown, reversalReference: "MOCK-DO-1"), true);

        var action = Assert.Single(actions);
        Assert.Equal("ReconcileReversal", action.Command);
        Assert.True(action.MovesMoney);
    }

    [Fact]
    public void FailedDebitReversalOffersRejectionOnly()
    {
        var actions = StaffActionCatalog.For(Details(TransactionCategory.DebitOrder,
            DisputeCaseStatus.ReversalRequested, DisputeDecisionStatus.Approved,
            DisputeFinancialStatus.Failed, reversalReference: "MOCK-DO-1"), true);

        Assert.Equal(["Reject"], actions.Select(action => action.Command));
    }

    [Theory]
    [InlineData(TransactionCategory.DebitOrder, DisputeCaseStatus.Submitted, "StartReview")]
    [InlineData(TransactionCategory.BankCharge, DisputeCaseStatus.ManualReview, "StartReview")]
    [InlineData(TransactionCategory.CardPurchase, DisputeCaseStatus.Submitted, "Initiate")]
    public void NewCasesStartWithTheCategoryEntryCommand(
        TransactionCategory category, DisputeCaseStatus status, string expected)
    {
        var actions = StaffActionCatalog.For(Details(
            category, status, DisputeDecisionStatus.Pending, DisputeFinancialStatus.NotInitiated), true);

        Assert.Equal([expected], actions.Select(action => action.Command));
    }

    [Fact]
    public void ActiveEscalationAndClosedCasesOfferNoCommands()
    {
        var escalated = Details(TransactionCategory.BankCharge, DisputeCaseStatus.UnderReview,
            DisputeDecisionStatus.Pending, DisputeFinancialStatus.NotInitiated) with
        {
            ActiveEscalation = new AgentEscalationSummary(Guid.NewGuid(), "ESC-1", "reason", "Open", DateTimeOffset.UnixEpoch)
        };
        var closed = Details(TransactionCategory.BankCharge, DisputeCaseStatus.Closed,
            DisputeDecisionStatus.Approved, DisputeFinancialStatus.Confirmed);

        Assert.Empty(StaffActionCatalog.For(escalated, true));
        Assert.Empty(StaffActionCatalog.For(closed, true));
    }

    [Fact]
    public void SimulatedMerchantEventsAreHiddenWhenSimulationsAreDisabled()
    {
        var item = Details(TransactionCategory.CardPurchase, DisputeCaseStatus.UnderReview,
            DisputeDecisionStatus.Pending, DisputeFinancialStatus.NotInitiated,
            cardStatus: "MerchantReview");

        Assert.Equal(["MerchantAccept", "MerchantContest"],
            StaffActionCatalog.For(item, true).Select(action => action.Command));
        Assert.Empty(StaffActionCatalog.For(item, false));
    }

    private static AgentDisputeDetails Details(
        TransactionCategory category, DisputeCaseStatus status, DisputeDecisionStatus decision,
        DisputeFinancialStatus financial, string? reversalReference = null, string? cardStatus = null) =>
        new(Guid.NewGuid(), "DSP-1", "CUST-1", category, "reason", "statement",
            DisputeEligibilityOutcome.EligibleToSubmit, "policy-v1", status, decision, financial,
            DateTimeOffset.UnixEpoch, 2,
            new AgentTransactionDetails("Test", 1000, "ZAR", DateTimeOffset.UnixEpoch, null, null, null, null),
            reversalReference, null,
            cardStatus is null ? null : new AgentCardWorkflow("MOCK-CB-1", "reason", cardStatus, "Succeeded"),
            null, [], []);
}

public sealed class HostingHardeningTests
{
    [Theory]
    [InlineData("trace-123_ABC")]
    [InlineData("employee-command-correlation")]
    public async Task ValidCorrelationIdIsEchoedUnchanged(string supplied)
    {
        using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", supplied);

        using var response = await client.SendAsync(request);

        Assert.Equal(supplied, response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Theory]
    [InlineData("bad id")]
    [InlineData("semi;colon")]
    [InlineData("emoji-\u2764")]
    public async Task CorrelationIdWithUnexpectedCharactersIsReplaced(string supplied)
    {
        using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", supplied);

        using var response = await client.SendAsync(request);

        var returned = response.Headers.GetValues("X-Correlation-ID").Single();
        Assert.NotEqual(supplied, returned);
        Assert.Matches("^[0-9a-f]{32}$", returned);
    }

    [Fact]
    public async Task OverlongCorrelationIdIsReplaced()
    {
        using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", new string('a', 65));

        using var response = await client.SendAsync(request);

        Assert.Matches("^[0-9a-f]{32}$", response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task AntiforgeryCookieIsHttpOnlyAndStrict()
    {
        await using var factory = new SqliteWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { username = "customer.demo", password = TestIdentityPasswords.Seed["Customer"] });

        var cookies = login.Headers.GetValues("Set-Cookie").ToArray();
        var csrf = Assert.Single(cookies, cookie => cookie.StartsWith("clearcase.csrf=", StringComparison.Ordinal));
        Assert.Contains("httponly", csrf, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", csrf, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GlobalRateLimitRejectsExcessRequestsButNeverHealthProbes()
    {
        await using var factory = new SqliteWebApplicationFactory();
        await factory.InitializeAsync();
        // UseSetting, unlike ConfigureAppConfiguration, is visible to configuration read while the host is being built.
        using var app = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimiting:RequestsPerMinute", "3"));
        using var client = app.CreateClient();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var allowed = await client.GetAsync("/api/v1/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }
        using var limited = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
        using var health = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public void SyntheticAuthenticationFallbackRefusesToStartOutsideTesting()
    {
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Authentication:EnableDevelopmentCompatibilityPath", "true");
        });

        var failure = Assert.Throws<InvalidOperationException>(() => app.CreateClient());
        Assert.Contains("Testing environment", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedTrustedProxyNetworkRefusesToStart()
    {
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Security:TrustedProxyNetworks:0", "not-a-cidr");
        });

        var failure = Assert.Throws<InvalidOperationException>(() => app.CreateClient());
        Assert.Contains("CIDR", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProductionAppliesMigrationsOnlyWhenExplicitlyEnabled()
    {
        var path = Path.Combine(Path.GetTempPath(), "clearcase-migrate-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, await ReadinessAsync(path, migrate: false));
            Assert.Equal(HttpStatusCode.OK, await ReadinessAsync(path, migrate: true));
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
                File.Delete(path + suffix);
        }
    }

    private static async Task<HttpStatusCode> ReadinessAsync(string path, bool migrate)
    {
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Persistence:DatabasePath"] = path,
                    ["Persistence:MigrateOnStartup"] = migrate.ToString()
                }));
        });
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var response = await client.GetAsync("/health/ready");
        return response.StatusCode;
    }
}
