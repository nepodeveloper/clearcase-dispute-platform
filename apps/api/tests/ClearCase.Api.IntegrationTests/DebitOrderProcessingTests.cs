using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Concurrent;
using ClearCase.Application.DisputeEligibility;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;
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

public sealed class DebitOrderProcessingTests : IAsyncLifetime
{
    private readonly SqliteWebApplicationFactory _database = new();
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _key = Guid.NewGuid().ToString("N");
    private readonly RecordingGateway _gateway = new();

    public DebitOrderProcessingTests()
    {
        _app = _database.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Simulation:DebitOrderKey"] = _key }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedClock());
                services.RemoveAll<IDebitOrderProcessingGateway>();
                services.AddSingleton<IDebitOrderProcessingGateway>(_gateway);
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
    public async Task Reversal_SeparatesApprovalFromConfirmation_AndPersistsEveryTransition()
    {
        using var client = Client();
        var id = await Submit(client);
        await Command(client, id, "StartReview", "UnderReview");
        await Command(client, id, "VerifyMandate", "MandateVerified");
        var requested = await Command(client, id, "RequestReversal", "ReversalRequested");
        Assert.Equal("Approved", requested.GetProperty("decisionStatus").GetString());
        Assert.Equal("Requested", requested.GetProperty("financialStatus").GetString());
        var confirmed = await Command(client, id, "ExecuteReversal", "ReversalConfirmed");
        Assert.Equal("Confirmed", confirmed.GetProperty("financialStatus").GetString());
        await Command(client, id, "Close", "Closed");

        using var reader = Client(simulator: false);
        var stored = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/disputes/{id}");
        Assert.Equal("Confirmed", stored.GetProperty("financialStatus").GetString());
        Assert.Equal(
            new[] { "Submitted", "UnderReview", "MandateVerified", "ReversalRequested", "ReversalConfirmed", "Closed" },
            stored.GetProperty("timeline").EnumerateArray().Select(item => item.GetProperty("caseStatus").GetString()));
        Assert.Equal(Enumerable.Range(1, 6),
            stored.GetProperty("timeline").EnumerateArray().Select(item => item.GetProperty("sequenceNumber").GetInt32()));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(11)]
    [InlineData(23)]
    [InlineData(5)]
    public async Task MissingMismatchDebiCheckOrEnhancedReview_CannotBeReversed(int transaction)
    {
        using var client = Client();
        var id = await Submit(client, transaction);
        await Command(client, id, "StartReview", "UnderReview");
        var investigation = await Command(client, id, "VerifyMandate", "UnderReview");
        Assert.Equal("NotInitiated", investigation.GetProperty("financialStatus").GetString());
        await Conflict(client, id, "CompleteInvestigation");
        await Conflict(client, id, "RequestReversal");
        await Command(client, id, "Reject", "Rejected");
        var closed = await Command(client, id, "Close", "Closed");
        Assert.Equal("Rejected", closed.GetProperty("decisionStatus").GetString());
        Assert.Equal("NotInitiated", closed.GetProperty("financialStatus").GetString());
    }

    [Fact]
    public async Task AuthorizationComplaint_RequiresExplicitSimulatedInvestigation()
    {
        using var client = Client();
        var id = await Submit(client, reason: "unauthorisedDebitOrder");
        await Command(client, id, "StartReview", "UnderReview");
        await Command(client, id, "VerifyMandate", "UnderReview");
        await Conflict(client, id, "RequestReversal");
        await Command(client, id, "CompleteInvestigation", "MandateVerified");
        await Command(client, id, "RequestReversal", "ReversalRequested");
    }

    [Fact]
    public async Task InvalidTransition_AndRepeatedExecution_DoNotAppendHistory()
    {
        using var client = Client();
        var id = await Submit(client);
        await Conflict(client, id, "ExecuteReversal");
        await Command(client, id, "StartReview", "UnderReview");
        await Command(client, id, "VerifyMandate", "MandateVerified");
        await Command(client, id, "RequestReversal", "ReversalRequested");
        await Command(client, id, "ExecuteReversal", "ReversalConfirmed");
        await Conflict(client, id, "ExecuteReversal");
        var stored = await client.GetFromJsonAsync<JsonElement>($"/api/v1/disputes/{id}");
        Assert.Equal(5, stored.GetProperty("timeline").GetArrayLength());
        Assert.Single(_gateway.References);
    }

    [Theory]
    [InlineData(DebitOrderGatewayOutcome.Failed, "Failed")]
    [InlineData(DebitOrderGatewayOutcome.Unknown, "OutcomeUnknown")]
    public async Task GatewayFailureOrUnknown_NeverConfirmsAndCannotExecuteAgainAfterRestart(
        DebitOrderGatewayOutcome outcome, string financialStatus)
    {
        _gateway.Outcome = outcome;
        using var client = Client();
        var id = await ReadyForReversal(client);
        var result = await Command(client, id, "ExecuteReversal", "ReversalRequested");
        Assert.Equal(financialStatus, result.GetProperty("financialStatus").GetString());
        Assert.Equal("Approved", result.GetProperty("decisionStatus").GetString());
        await Conflict(client, id, "Close");

        using var restarted = _app.WithWebHostBuilder(_ => { });
        using var restartedClient = restarted.CreateClient();
        restartedClient.DefaultRequestHeaders.Add("X-Simulation-Key", _key);
        await AddCsrfTokenAsync(restartedClient);
        await Conflict(restartedClient, id, "ExecuteReversal");
        await Conflict(restartedClient, id, "RequestReversal");
        if (outcome == DebitOrderGatewayOutcome.Unknown)
            await Conflict(restartedClient, id, "Reject");
        else
            await Command(restartedClient, id, "Reject", "Rejected");

        using var scope = restarted.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<TransactionDbContext>().Disputes
            .AsNoTracking().SingleAsync(item => item.Id == id);
        Assert.Equal($"MOCK-DO-{id:N}", stored.ReversalOperationReference);
        Assert.Equal(stored.ReversalOperationReference, Assert.Single(_gateway.References));
    }

    [Fact]
    public async Task ConcurrentExecution_CommitsOneGatewayOutcomeAndOneTimelineEvent()
    {
        using var client = Client();
        var id = await ReadyForReversal(client);
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync(Route(id), new { command = "ExecuteReversal" }),
            client.PostAsJsonAsync(Route(id), new { command = "ExecuteReversal" }));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            Assert.Single(_gateway.References);
            var stored = await client.GetFromJsonAsync<JsonElement>($"/api/v1/disputes/{id}");
            Assert.Equal(5, stored.GetProperty("timeline").GetArrayLength());
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task TimelineFailure_RollsBackStateAndOperationReference()
    {
        using var client = Client();
        var id = await Submit(client);
        await Command(client, id, "StartReview", "UnderReview");
        await Command(client, id, "VerifyMandate", "MandateVerified");
        using var scope = _app.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        await database.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER "TR_Test_FailProcessingEvent" BEFORE INSERT ON "DisputeEvents"
            BEGIN SELECT RAISE(ABORT, 'simulated timeline failure'); END;
            """);

        using var response = await client.PostAsJsonAsync(Route(id), new { command = "RequestReversal" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var stored = await database.Disputes.AsNoTracking().SingleAsync(item => item.Id == id);
        Assert.Equal(DisputeCaseStatus.MandateVerified, stored.CaseStatus);
        Assert.Equal(DisputeDecisionStatus.Pending, stored.DecisionStatus);
        Assert.Equal(DisputeFinancialStatus.NotInitiated, stored.FinancialStatus);
        Assert.Null(stored.ReversalOperationReference);
        Assert.Equal(3, await database.DisputeEvents.CountAsync(item => item.DisputeId == id));
        Assert.Empty(_gateway.References);
    }

    [Fact]
    public async Task ChangedMandate_IsRecheckedBeforeRequestAndBeforeExecution()
    {
        using var client = Client();
        var id = await Submit(client);
        await Command(client, id, "StartReview", "UnderReview");
        await Command(client, id, "VerifyMandate", "MandateVerified");
        using var changed = _app.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDebitOrderMandateReader>();
            services.AddSingleton<IDebitOrderMandateReader>(new MissingMandateReader());
        }));
        using var missing = changed.CreateClient();
        missing.DefaultRequestHeaders.Add("X-Simulation-Key", _key);
        await AddCsrfTokenAsync(missing);
        await Conflict(missing, id, "RequestReversal");
        await Command(client, id, "RequestReversal", "ReversalRequested");
        await Conflict(missing, id, "ExecuteReversal");
        Assert.Empty(_gateway.References);
    }

    [Fact]
    public async Task Simulation_RejectsInvalidCommandsAndNonDebitOrderCases()
    {
        using var client = Client();
        var id = await Submit(client, 1, "duplicateCharge");
        await Conflict(client, id, "StartReview");
        foreach (var command in new[] { "ReversalConfirmed", "0", "startReview", "", null })
        {
            using var response = await client.PostAsJsonAsync(Route(id), new { command });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("/command", body.GetProperty("errors")[0].GetProperty("pointer").GetString());
        }
    }

    [Fact]
    public async Task Simulation_WithoutConfiguredKeyIsForbidden_AndOpenApiDescribesCommands()
    {
        using var host = _app.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Simulation:DebitOrderKey"] = null }));
        });
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Simulation-Key", _key);
        await AddCsrfTokenAsync(client);
        using var denied = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new { command = "StartReview" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var operation = document.GetProperty("paths")
            .GetProperty("/api/v1/disputes/{id}/debit-order-simulations").GetProperty("post");
        Assert.Contains("X-Simulation-Key", operation.GetProperty("description").GetString());
        Assert.True(operation.GetProperty("responses").TryGetProperty("409", out _));
    }

    [Fact]
    public async Task Simulation_RejectsOrdinaryCustomerWrongKeyAndOtherCustomerCase()
    {
        using var client = Client();
        var id = await Submit(client);
        using var customer = Client(simulator: false);
        using var denied = await customer.PostAsJsonAsync(Route(id), new { command = "StartReview" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        customer.DefaultRequestHeaders.Add("X-Simulation-Key", "incorrect");
        using var wrongKey = await customer.PostAsJsonAsync(Route(id), new { command = "StartReview" });
        Assert.Equal(HttpStatusCode.Forbidden, wrongKey.StatusCode);

        using var otherApp = _app.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Demo:CustomerId"] = "22222222-2222-2222-2222-222222222222"
                })));
        using var other = otherApp.CreateClient();
        other.DefaultRequestHeaders.Add("X-Simulation-Key", _key);
        await AddCsrfTokenAsync(other);
        using var notFound = await other.PostAsJsonAsync(Route(id), new { command = "StartReview" });
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task Simulation_RejectsBankEmployeeIdentityBeforeHandlerExecution()
    {
        using var host = _app.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Authentication:EnableDevelopmentCompatibilityPath"] = "false" })));
        using var client = host.CreateClient();
        await LoginAsync(client, "agent.demo", "ClearCase!Agent1");
        client.DefaultRequestHeaders.Add("X-Simulation-Key", _key);

        using var response = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new { command = "StartReview" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Simulation_DoesNotExistOutsideDemoEnvironments(string environment)
    {
        using var host = _app.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Simulation-Key", _key);
        using var response = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new { command = "StartReview" });
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.Unauthorized });
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

    private static async Task LoginAsync(HttpClient client, string username, string password)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("csrfToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
    }

    private static async Task<Guid> Submit(HttpClient client, int transaction = 2, string reason = "duplicateCollection")
    {
        using var response = await client.PostAsJsonAsync("/api/v1/disputes", new
        {
            transactionId = $"00000000-0000-0000-0000-{transaction:D12}",
            reasonCode = reason,
            description = "This collection needs investigation for a duplicate debit."
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static string Route(Guid id) => $"/api/v1/disputes/{id}/debit-order-simulations";

    private static async Task<Guid> ReadyForReversal(HttpClient client)
    {
        var id = await Submit(client);
        await Command(client, id, "StartReview", "UnderReview");
        await Command(client, id, "VerifyMandate", "MandateVerified");
        await Command(client, id, "RequestReversal", "ReversalRequested");
        return id;
    }

    private static async Task<JsonElement> Command(HttpClient client, Guid id, string command, string status)
    {
        using var response = await client.PostAsJsonAsync(Route(id), new { command });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(status, body.GetProperty("caseStatus").GetString());
        return body;
    }

    private static async Task Conflict(HttpClient client, Guid id, string command)
    {
        using var response = await client.PostAsJsonAsync(Route(id), new { command });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("debitOrder.invalidTransition", body.GetProperty("code").GetString());
        Assert.True(body.TryGetProperty("correlationId", out _));
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
    }

    private sealed class MissingMandateReader : IDebitOrderMandateReader
    {
        public Task<DebitOrderMandate?> ReadAsync(Guid customerId, Guid transactionId, CancellationToken cancellationToken) =>
            Task.FromResult<DebitOrderMandate?>(null);
    }

    private sealed class RecordingGateway : IDebitOrderProcessingGateway
    {
        public DebitOrderGatewayOutcome Outcome { get; set; } = DebitOrderGatewayOutcome.Confirmed;
        public ConcurrentQueue<string> References { get; } = new();

        public Task<DebitOrderGatewayOutcome> ReverseAsync(string operationReference, CancellationToken cancellationToken)
        {
            References.Enqueue(operationReference);
            return new MockDebitOrderProcessingGateway(Outcome).ReverseAsync(operationReference, cancellationToken);
        }
    }
}
