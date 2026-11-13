using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Concurrent;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Disputes;
using ClearCase.Infrastructure.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Api.IntegrationTests;

public sealed class DisputeTrackingTests : IAsyncLifetime
{
    private readonly SqliteWebApplicationFactory _database = new();
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _key = Guid.NewGuid().ToString("N");
    private readonly FixedClock _clock = new();
    private readonly RecordingGateway _gateway = new();

    public DisputeTrackingTests()
    {
        _app = _database.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Simulation:BankChargeKey"] = _key,
                    ["Simulation:DebitOrderKey"] = _key,
                    ["Simulation:CardNetworkKey"] = _key
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(_clock);
                services.RemoveAll<IBankChargeAdjustmentGateway>();
                services.AddSingleton<IBankChargeAdjustmentGateway>(_gateway);
            });
        });
    }

    public Task InitializeAsync() => _database.InitializeAsync();

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await ((IAsyncLifetime)_database).DisposeAsync();
    }

    [Fact]
    public async Task BankApproval_SeparatesDecisionPendingAdjustmentAndConfirmation()
    {
        using var client = Client();
        var id = (await Submit(client, 3, "incorrectFee")).GetProperty("id").GetGuid();
        await Command(client, id, "StartReview", "UnderReview", "Pending", "NotInitiated");
        await Command(client, id, "Approve", "UnderReview", "Approved", "NotInitiated");
        await Conflict(client, id, "ExecuteAdjustment");
        var pending = await Command(client, id, "RequestAdjustment", "UnderReview", "Approved", "Requested");
        Assert.Contains("not confirmed", pending.GetProperty("statusDescription").GetString());
        await Conflict(client, id, "Close");
        await Command(client, id, "ExecuteAdjustment", "UnderReview", "Approved", "Confirmed");
        await Conflict(client, id, "ExecuteAdjustment");
        await Command(client, id, "Close", "Closed", "Approved", "Confirmed");

        using var customer = Client(simulator: false);
        var events = await customer.GetFromJsonAsync<JsonElement>($"/v1/disputes/{id}/events");
        Assert.Equal(Enumerable.Range(1, 6), events.EnumerateArray().Select(item => item.GetProperty("sequenceNumber").GetInt32()));
        Assert.All(events.EnumerateArray(), item =>
        {
            Assert.Equal("2026-11-13T07:00:00+00:00", item.GetProperty("occurredAt").GetString());
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("description").GetString()));
        });
    }

    [Fact]
    public async Task BankRejection_CannotRequestOrConfirmAdjustment()
    {
        using var client = Client();
        var id = (await Submit(client, 3, "incorrectFee")).GetProperty("id").GetGuid();
        await Conflict(client, id, "Approve");
        await Command(client, id, "StartReview", "UnderReview", "Pending", "NotInitiated");
        await Command(client, id, "Reject", "Rejected", "Rejected", "NotInitiated");
        await Conflict(client, id, "RequestAdjustment");
        await Conflict(client, id, "ExecuteAdjustment");
        await Command(client, id, "Close", "Closed", "Rejected", "NotInitiated");
    }

    [Theory]
    [InlineData(BankChargeAdjustmentOutcome.Confirmed, "Confirmed")]
    [InlineData(BankChargeAdjustmentOutcome.Failed, "Failed")]
    [InlineData(BankChargeAdjustmentOutcome.Unknown, "OutcomeUnknown")]
    public async Task ConcurrentAdjustment_CommitsOneOutcomeAndCannotReplayAfterRestart(
        BankChargeAdjustmentOutcome outcome, string financial)
    {
        _gateway.Outcome = outcome;
        using var client = Client();
        var id = (await Submit(client, 3, "incorrectFee")).GetProperty("id").GetGuid();
        await Command(client, id, "StartReview", "UnderReview", "Pending", "NotInitiated");
        await Command(client, id, "Approve", "UnderReview", "Approved", "NotInitiated");
        await Command(client, id, "RequestAdjustment", "UnderReview", "Approved", "Requested");
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync(Route(id), new { command = "ExecuteAdjustment" }),
            client.PostAsJsonAsync(Route(id), new { command = "ExecuteAdjustment" }));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var response in responses) response.Dispose(); }
        using var restarted = _app.WithWebHostBuilder(_ => { });
        using var replay = restarted.CreateClient();
        replay.DefaultRequestHeaders.Add("X-Simulation-Key", _key);
        await AddCsrfTokenAsync(replay);
        await Conflict(replay, id, "ExecuteAdjustment");
        if (outcome != BankChargeAdjustmentOutcome.Confirmed) await Conflict(replay, id, "Close");
        var result = await replay.GetFromJsonAsync<JsonElement>($"/api/v1/disputes/{id}");
        Assert.Equal(financial, result.GetProperty("financialStatus").GetString());
        Assert.Equal(5, result.GetProperty("timeline").GetArrayLength());
        Assert.Equal($"MOCK-FEE-{id:N}", Assert.Single(_gateway.References));
    }

    [Fact]
    public async Task TimelineFailure_RollsBackAdjustmentIntentAndCaseUpdate()
    {
        using var client = Client();
        var id = (await Submit(client, 3, "incorrectFee")).GetProperty("id").GetGuid();
        await Command(client, id, "StartReview", "UnderReview", "Pending", "NotInitiated");
        await Command(client, id, "Approve", "UnderReview", "Approved", "NotInitiated");
        using var scope = _app.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        await database.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER "TR_Test_FailFeeEvent" BEFORE INSERT ON "DisputeEvents"
            BEGIN SELECT RAISE(ABORT, 'simulated timeline failure'); END;
            """);
        using var failed = await client.PostAsJsonAsync(Route(id), new { command = "RequestAdjustment" });
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        var stored = await database.Disputes.AsNoTracking().SingleAsync(item => item.Id == id);
        Assert.Null(stored.BankChargeAdjustmentReference);
        Assert.Equal(DisputeFinancialStatus.NotInitiated, stored.FinancialStatus);
        Assert.Equal(3, await database.DisputeEvents.CountAsync(item => item.DisputeId == id));
    }

    [Fact]
    public async Task Cursor_UsesNewestFirstAndDoesNotRepeatCasesWhenNewOnesAreSubmitted()
    {
        using var client = Client();
        var oldest = await Submit(client, 1, "duplicateCharge");
        _clock.Now = _clock.Now.AddMinutes(1);
        var second = await Submit(client, 2, "duplicateCollection");
        var firstPage = await client.GetFromJsonAsync<JsonElement>("/v1/disputes?pageSize=1");
        Assert.Equal(second.GetProperty("id").GetGuid(), firstPage.GetProperty("items")[0].GetProperty("id").GetGuid());
        var cursor = firstPage.GetProperty("pageInfo").GetProperty("nextCursor").GetString()!;
        _clock.Now = _clock.Now.AddMinutes(1);
        var newest = await Submit(client, 3, "incorrectFee");
        var nextPage = await client.GetFromJsonAsync<JsonElement>("/v1/disputes?pageSize=1&cursor=" + Uri.EscapeDataString(cursor));
        Assert.Equal(oldest.GetProperty("id").GetGuid(), nextPage.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.False(nextPage.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean());
        var refreshed = await client.GetFromJsonAsync<JsonElement>("/api/v1/disputes?pageSize=100");
        Assert.Equal(newest.GetProperty("id").GetGuid(), refreshed.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(3, refreshed.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task OpenApi_DocumentsBothQueryAliasesAndSafeProjections()
    {
        using var host = _app.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = host.CreateClient();
        await AddCsrfTokenAsync(client);
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = document.GetProperty("paths");
        foreach (var prefix in new[] { "/v1", "/api/v1" })
        {
            Assert.True(paths.GetProperty(prefix + "/disputes").TryGetProperty("get", out _));
            Assert.True(paths.GetProperty(prefix + "/disputes/{id}/events").TryGetProperty("get", out _));
        }
        Assert.True(paths.TryGetProperty("/api/v1/disputes/{id}/bank-charge-simulations", out _));
        using var invalid = await client.GetAsync("/v1/disputes/not-a-guid/events");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Workflows_RejectOtherDisputeCategories()
    {
        using var client = Client();
        var fee = (await Submit(client, 3, "incorrectFee")).GetProperty("id").GetGuid();
        foreach (var (workflow, command) in new[] { ("chargeback", "Initiate"), ("debit-order", "StartReview") })
        {
            using var response = await client.PostAsJsonAsync($"/api/v1/disputes/{fee}/{workflow}-simulations", new { command });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
        foreach (var (transaction, reason) in new[] { (1, "duplicateCharge"), (2, "duplicateCollection") })
        {
            var id = (await Submit(client, transaction, reason)).GetProperty("id").GetGuid();
            await Conflict(client, id, "StartReview");
        }
    }

    [Fact]
    public async Task ListAndEvents_AreOwnedAndCursorPagesAreStableForEqualTimestamps()
    {
        using var client = Client();
        var cases = new[]
        {
            await Submit(client, 1, "duplicateCharge"),
            await Submit(client, 2, "duplicateCollection"),
            await Submit(client, 3, "incorrectFee")
        };
        var expected = cases.OrderBy(item => item.GetProperty("reference").GetString(), StringComparer.Ordinal)
            .Select(item => item.GetProperty("id").GetGuid()).ToArray();
        var actual = new List<Guid>();
        string? cursor = null;
        do
        {
            using var response = await client.GetAsync("/v1/disputes?pageSize=1"
                + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var page = await response.Content.ReadFromJsonAsync<JsonElement>();
            var item = Assert.Single(page.GetProperty("items").EnumerateArray());
            actual.Add(item.GetProperty("id").GetGuid());
            Assert.False(item.TryGetProperty("description", out _));
            Assert.False(item.TryGetProperty("timeline", out _));
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("statusDescription").GetString()));
            var pageInfo = page.GetProperty("pageInfo");
            cursor = pageInfo.TryGetProperty("nextCursor", out var next) ? next.GetString() : null;
            Assert.Equal(cursor is not null, pageInfo.GetProperty("hasNextPage").GetBoolean());
        } while (cursor is not null && actual.Count <= 3);
        Assert.Equal(expected, actual);

        using var otherApp = _app.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Demo:CustomerId"] = "22222222-2222-2222-2222-222222222222"
            })));
        using var other = otherApp.CreateClient();
        await AddCsrfTokenAsync(other);
        var empty = await other.GetFromJsonAsync<JsonElement>("/api/v1/disputes");
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        Assert.False(empty.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean());
        using var denied = await other.GetAsync($"/v1/disputes/{expected[0]}/events");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        other.DefaultRequestHeaders.Add("X-Simulation-Key", _key);
        using var processingDenied = await other.PostAsJsonAsync(Route(cases[2].GetProperty("id").GetGuid()), new { command = "StartReview" });
        Assert.Equal(HttpStatusCode.NotFound, processingDenied.StatusCode);
    }

    [Theory]
    [InlineData("?pageSize=0", "/pageSize")]
    [InlineData("?pageSize=101", "/pageSize")]
    [InlineData("?pageSize=invalid", "/pageSize")]
    [InlineData("?cursor=invalid", "/cursor")]
    public async Task List_InvalidQueryReturnsProblemDetails(string query, string pointer)
    {
        using var client = Client(false);
        using var response = await client.GetAsync("/api/v1/disputes" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("dispute.invalidQuery", problem.GetProperty("code").GetString());
        Assert.Equal(pointer, problem.GetProperty("errors")[0].GetProperty("pointer").GetString());
        Assert.True(problem.TryGetProperty("correlationId", out _));
    }

    [Fact]
    public async Task BankSimulation_RejectsOrdinaryCustomer()
    {
        using var client = Client(false);
        var id = (await Submit(client, 3, "incorrectFee")).GetProperty("id").GetGuid();
        using var response = await client.PostAsJsonAsync(Route(id), new { command = "StartReview" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/v1/disputes")]
    [InlineData("/api/v1/disputes")]
    [InlineData("/v1/disputes/aaaaaaaa-1111-4222-8333-444455556666/events")]
    public async Task Queries_RequireAuthentication(string route)
    {
        using var production = _app.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = production.CreateClient();
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var absent = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new { command = "StartReview" });
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
    }

    private HttpClient Client(bool simulator = true)
    {
        var client = _app.CreateClient();
        if (simulator) client.DefaultRequestHeaders.Add("X-Simulation-Key", _key);
        AddCsrfTokenAsync(client).GetAwaiter().GetResult();
        return client;
    }

    private static async Task AddCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("csrfToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
    }

    private static string Route(Guid id) => $"/api/v1/disputes/{id}/bank-charge-simulations";

    private static async Task<JsonElement> Submit(HttpClient client, int transaction, string reason)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/disputes", new
        {
            transactionId = $"00000000-0000-0000-0000-{transaction:D12}", reasonCode = reason,
            description = "Please investigate this transaction and explain the charge."
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> Command(HttpClient client, Guid id, string command,
        string status, string decision, string financial)
    {
        using var response = await client.PostAsJsonAsync(Route(id), new { command });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(status, result.GetProperty("caseStatus").GetString());
        Assert.Equal(decision, result.GetProperty("decisionStatus").GetString());
        Assert.Equal(financial, result.GetProperty("financialStatus").GetString());
        return result;
    }

    private static async Task Conflict(HttpClient client, Guid id, string command)
    {
        using var response = await client.PostAsJsonAsync(Route(id), new { command });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 13, 7, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingGateway : IBankChargeAdjustmentGateway
    {
        public BankChargeAdjustmentOutcome Outcome { get; set; } = BankChargeAdjustmentOutcome.Confirmed;
        public ConcurrentQueue<string> References { get; } = new();

        public Task<BankChargeAdjustmentOutcome> AdjustAsync(string operationReference, CancellationToken cancellationToken)
        {
            References.Enqueue(operationReference);
            return new MockBankChargeAdjustmentGateway(Outcome).AdjustAsync(operationReference, cancellationToken);
        }
    }
}
