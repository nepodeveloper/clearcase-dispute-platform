using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Concurrent;
using ClearCase.Application.Chargebacks;
using ClearCase.Domain.Chargebacks;
using ClearCase.Domain.DisputeEligibility;
using ClearCase.Domain.Disputes;
using ClearCase.Infrastructure.Chargebacks;
using ClearCase.Infrastructure.Transactions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Api.IntegrationTests;

public sealed class ChargebackProcessingTests : IAsyncLifetime
{
    private static readonly Guid EvidenceId = Guid.Parse("aaaaaaaa-1111-4222-8333-444455556666");
    private readonly SqliteWebApplicationFactory _database = new();
    private readonly WebApplicationFactory<Program> _app;
    private readonly string _key = Guid.NewGuid().ToString("N");
    private readonly RecordingGateway _gateway = new();

    public ChargebackProcessingTests()
    {
        _app = _database.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Simulation:CardNetworkKey"] = _key }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedClock());
                services.RemoveAll<ICardNetworkGateway>();
                services.AddSingleton<ICardNetworkGateway>(_gateway);
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
    public async Task Initiation_RecordsReferenceAndReason_WithoutApprovingOrCreditingFunds()
    {
        using var client = Client();
        var id = await Submit(client);
        var result = await Command(client, id, "Initiate", "Initiated");

        Assert.Equal("UnderReview", result.GetProperty("caseStatus").GetString());
        Assert.Equal("Pending", result.GetProperty("decisionStatus").GetString());
        Assert.Equal("NotInitiated", result.GetProperty("financialStatus").GetString());
        Assert.Equal("duplicateCharge", result.GetProperty("chargeback").GetProperty("reasonCode").GetString());
        Assert.Equal($"MOCK-CB-{id:N}", result.GetProperty("chargeback").GetProperty("reference").GetString());
        Assert.False(result.GetProperty("chargeback").TryGetProperty("evidence", out _));
        Assert.Equal(2, result.GetProperty("timeline").GetArrayLength());
    }

    [Fact]
    public async Task MerchantAcceptance_RequiresExplicitSettlementBeforeClosure()
    {
        using var client = Client();
        var id = await MerchantReview(client);
        var accepted = await Command(client, id, "MerchantAccept", "MerchantAccepted");
        Assert.Equal("Approved", accepted.GetProperty("decisionStatus").GetString());
        Assert.Equal("NotInitiated", accepted.GetProperty("financialStatus").GetString());
        await Conflict(client, id, "Close");
        await Conflict(client, id, "ExecuteSettlement");
        var requested = await Command(client, id, "RequestSettlement", "MerchantAccepted");
        Assert.Equal("Requested", requested.GetProperty("financialStatus").GetString());
        await Conflict(client, id, "Close");
        var settled = await Command(client, id, "ExecuteSettlement", "MerchantAccepted");
        Assert.Equal("Confirmed", settled.GetProperty("financialStatus").GetString());
        var closed = await Command(client, id, "Close", "Closed");
        Assert.Equal("Closed", closed.GetProperty("caseStatus").GetString());
        Assert.Equal("Approved", closed.GetProperty("decisionStatus").GetString());
        Assert.Equal("Confirmed", closed.GetProperty("financialStatus").GetString());
        using var scope = _app.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        var audit = await database.ChargebackAuditEvents.AsNoTracking().Where(item => item.DisputeId == id).ToArrayAsync();
        Assert.Equal(6, audit.Length);
        Assert.All(audit, entry =>
        {
            Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), entry.ActorId);
            Assert.Equal("chargeback-simulation-test", entry.CorrelationId);
            Assert.Equal($"MOCK-CARD-{id:N}-{entry.Command}", entry.OperationReference);
            Assert.Equal(FixedClock.Instant, entry.OccurredAt);
            Assert.Contains(closed.GetProperty("timeline").EnumerateArray(),
                item => item.GetProperty("eventType").GetString() == $"Chargeback{entry.Command}");
        });
        Assert.Equal(3, _gateway.Requests.Count);
        Assert.Equal(Enumerable.Range(1, 7),
            closed.GetProperty("timeline").EnumerateArray().Select(item => item.GetProperty("sequenceNumber").GetInt32()));
    }

    [Theory]
    [InlineData("Approve", "Approved")]
    [InlineData("Reject", "Rejected")]
    public async Task Representment_RequiresEvidenceAndIssuerReview_BeforeFinalDecision(string action, string decision)
    {
        using var client = Client();
        var id = await MerchantReview(client);
        var contested = await Command(client, id, "MerchantContest", "MerchantContested");
        Assert.Equal("Pending", contested.GetProperty("decisionStatus").GetString());
        Assert.Equal("NotInitiated", contested.GetProperty("financialStatus").GetString());
        await Conflict(client, id, "Approve");
        await Conflict(client, id, "Reject");
        await Conflict(client, id, "ReviewRepresentment");
        var received = await Command(client, id, "ReceiveRepresentment", "RepresentmentReceived",
            new { id = EvidenceId, kind = "Receipt" });
        var evidence = received.GetProperty("chargeback").GetProperty("evidence");
        Assert.Equal(EvidenceId, evidence.GetProperty("id").GetGuid());
        Assert.Equal("Receipt", evidence.GetProperty("kind").GetString());
        Assert.Equal("2026-11-12T10:00:00+00:00", evidence.GetProperty("receivedAt").GetString());
        await Conflict(client, id, action);
        await Command(client, id, "ReviewRepresentment", "IssuerReview");
        var final = await Command(client, id, action, "DecisionIssued");
        Assert.Equal(decision, final.GetProperty("decisionStatus").GetString());
        Assert.Equal("NotInitiated", final.GetProperty("financialStatus").GetString());
        if (action == "Reject")
        {
            await Conflict(client, id, "RequestSettlement");
            await Command(client, id, "Close", "Closed");
        }
        else
        {
            await Conflict(client, id, "Close");
            await Command(client, id, "RequestSettlement", "DecisionIssued");
            var settled = await Command(client, id, "ExecuteSettlement", "DecisionIssued");
            Assert.Equal("Confirmed", settled.GetProperty("financialStatus").GetString());
        }
    }

    [Fact]
    public async Task InvalidAndRepeatedCommands_CannotSkipStagesOrAppendHistory()
    {
        using var client = Client();
        var id = await Submit(client);
        await Conflict(client, id, "ForwardToAcquirer");
        await Command(client, id, "Initiate", "Initiated");
        await Conflict(client, id, "Initiate");
        await Conflict(client, id, "MerchantAccept");
        var stored = await client.GetFromJsonAsync<JsonElement>($"/api/v1/disputes/{id}");
        Assert.Equal(2, stored.GetProperty("timeline").GetArrayLength());
        Assert.Single(_gateway.Requests);
    }

    [Theory]
    [InlineData(ChargebackCommand.Initiate, CardNetworkOutcome.Failed)]
    [InlineData(ChargebackCommand.Initiate, CardNetworkOutcome.OutcomeUnknown)]
    [InlineData(ChargebackCommand.ForwardToAcquirer, CardNetworkOutcome.Failed)]
    [InlineData(ChargebackCommand.ForwardToAcquirer, CardNetworkOutcome.OutcomeUnknown)]
    [InlineData(ChargebackCommand.ExecuteSettlement, CardNetworkOutcome.Failed)]
    [InlineData(ChargebackCommand.ExecuteSettlement, CardNetworkOutcome.OutcomeUnknown)]
    public async Task GatewayFailureOrTimeout_PersistsUnconfirmedOutcomeAndBlocksReplayAfterRestart(
        ChargebackCommand action, CardNetworkOutcome outcome)
    {
        using var client = Client();
        var id = await Submit(client);
        var status = "InitiationPending";
        if (action != ChargebackCommand.Initiate)
        {
            await Command(client, id, "Initiate", "Initiated");
            status = "Initiated";
        }
        if (action == ChargebackCommand.ExecuteSettlement)
        {
            await Command(client, id, "ForwardToAcquirer", "MerchantReview");
            await Command(client, id, "MerchantAccept", "MerchantAccepted");
            await Command(client, id, "RequestSettlement", "MerchantAccepted");
            status = "MerchantAccepted";
        }
        _gateway.Outcome = outcome;

        var result = await Command(client, id, action.ToString(), status);

        Assert.Equal(outcome.ToString(), result.GetProperty("chargeback").GetProperty("lastOperationOutcome").GetString());
        Assert.Equal(action == ChargebackCommand.ExecuteSettlement
                ? outcome == CardNetworkOutcome.Failed ? "Failed" : "OutcomeUnknown"
                : "NotInitiated",
            result.GetProperty("financialStatus").GetString());
        Assert.NotEqual("Confirmed", result.GetProperty("financialStatus").GetString());
        var callCount = _gateway.Requests.Count;
        using var restarted = _app.WithWebHostBuilder(_ => { });
        using var replay = restarted.CreateClient();
        replay.DefaultRequestHeaders.Add("X-Simulation-Key", _key);
        replay.DefaultRequestHeaders.Add("X-Correlation-ID", "chargeback-simulation-test");
        await AddCsrfTokenAsync(replay);
        await Conflict(replay, id, action.ToString());
        await Conflict(replay, id, "Close");
        Assert.Equal(callCount, _gateway.Requests.Count);
        var persisted = await replay.GetFromJsonAsync<JsonElement>($"/api/v1/disputes/{id}");
        Assert.Equal(result.GetProperty("financialStatus").GetString(), persisted.GetProperty("financialStatus").GetString());
        using var scope = restarted.Services.CreateScope();
        var audit = await scope.ServiceProvider.GetRequiredService<TransactionDbContext>().ChargebackAuditEvents
            .AsNoTracking().SingleAsync(item => item.DisputeId == id && item.Command == action);
        Assert.Equal(outcome, audit.Outcome);
        Assert.Equal($"MOCK-CARD-{id:N}-{action}", audit.OperationReference);
    }

    [Theory]
    [InlineData("Initiate")]
    [InlineData("ExecuteSettlement")]
    public async Task ConcurrentRequests_ExecuteOnceAndCommitOneAuditEvent(string action)
    {
        using var client = Client();
        var id = await Submit(client);
        if (action == "ExecuteSettlement")
        {
            await Command(client, id, "Initiate", "Initiated");
            await Command(client, id, "ForwardToAcquirer", "MerchantReview");
            await Command(client, id, "MerchantAccept", "MerchantAccepted");
            await Command(client, id, "RequestSettlement", "MerchantAccepted");
        }
        var responses = await Task.WhenAll(client.PostAsJsonAsync(Route(id), new { command = action }),
            client.PostAsJsonAsync(Route(id), new { command = action }));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            Assert.Single(_gateway.Requests, request => request.Command.ToString() == action);
            using var scope = _app.Services.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
            var command = Enum.Parse<ChargebackCommand>(action);
            Assert.Equal(1, await database.ChargebackAuditEvents.CountAsync(item => item.DisputeId == id && item.Command == command));
            var eventType = "Chargeback" + action;
            Assert.Equal(1, await database.DisputeEvents.CountAsync(item => item.DisputeId == id && item.EventType == eventType));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Theory]
    [InlineData("DisputeEvents", false)]
    [InlineData("ChargebackAuditEvents", false)]
    [InlineData("ChargebackAuditEvents", true)]
    public async Task InsertFailure_RollsBackCaseChargebackTimelineAndAudit(string table, bool existingChargeback)
    {
        using var client = Client();
        var id = await Submit(client);
        if (existingChargeback) await Command(client, id, "Initiate", "Initiated");
        using var scope = _app.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        var failInsert = table switch
        {
            "DisputeEvents" => """
                CREATE TRIGGER "TR_Test_FailChargebackInsert" BEFORE INSERT ON "DisputeEvents"
                BEGIN SELECT RAISE(ABORT, 'simulated insert failure'); END;
                """,
            "ChargebackAuditEvents" => """
                CREATE TRIGGER "TR_Test_FailChargebackInsert" BEFORE INSERT ON "ChargebackAuditEvents"
                BEGIN SELECT RAISE(ABORT, 'simulated insert failure'); END;
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        };
        await database.Database.ExecuteSqlRawAsync(failInsert);

        using var response = await client.PostAsJsonAsync(Route(id),
            new { command = existingChargeback ? "ForwardToAcquirer" : "Initiate" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var stored = await database.Disputes.AsNoTracking().SingleAsync(item => item.Id == id);
        Assert.Equal(existingChargeback ? DisputeCaseStatus.UnderReview : DisputeCaseStatus.Submitted, stored.CaseStatus);
        Assert.Equal(DisputeFinancialStatus.NotInitiated, stored.FinancialStatus);
        Assert.Equal(existingChargeback ? 1 : 0, await database.Chargebacks.CountAsync());
        Assert.Equal(existingChargeback ? 1 : 0, await database.ChargebackAuditEvents.CountAsync());
        Assert.Equal(existingChargeback ? 2 : 1, await database.DisputeEvents.CountAsync(item => item.DisputeId == id));
        if (existingChargeback)
            Assert.Equal(ChargebackStatus.Initiated, (await database.Chargebacks.SingleAsync()).Status);
    }

    [Fact]
    public async Task AuditIsAppendOnly_AndOperationReferencesAreUnique()
    {
        using var client = Client();
        var id = await Submit(client);
        await Command(client, id, "Initiate", "Initiated");
        using var scope = _app.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        var audit = await database.ChargebackAuditEvents.SingleAsync();
        audit.CorrelationId = "rewritten";
        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
            database.Database.ExecuteSqlRawAsync("DELETE FROM \"ChargebackAuditEvents\""));
        var duplicate = await database.ChargebackAuditEvents.AsNoTracking().SingleAsync();
        duplicate.Id = Guid.NewGuid();
        database.ChargebackAuditEvents.Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
        Assert.Equal(1, await database.ChargebackAuditEvents.CountAsync());
    }

    [Fact]
    public async Task NonCardCasesAndChangedEligibility_CannotInitiateChargebacks()
    {
        using var client = Client();
        var debitOrder = await Submit(client, 2, "duplicateCollection");
        await Conflict(client, debitOrder, "Initiate");
        var card = await Submit(client);
        using var changed = _app.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DisputeEligibilityPolicySettings>();
            services.AddSingleton(new DisputeEligibilityPolicySettings { DemoPoliciesEnabled = false });
        }));
        using var ineligible = changed.CreateClient();
        ineligible.DefaultRequestHeaders.Add("X-Simulation-Key", _key);
        ineligible.DefaultRequestHeaders.Add("X-Correlation-ID", "chargeback-simulation-test");
        await AddCsrfTokenAsync(ineligible);
        await Conflict(ineligible, card, "Initiate");
        Assert.Empty(_gateway.Requests);
    }

    [Fact]
    public async Task NoConfiguredKeyDeniesAccess_AndOpenApiDescribesAdditiveContract()
    {
        using var host = _app.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Simulation:CardNetworkKey"] = null }));
        });
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Simulation-Key", _key);
        await TestIdentityPasswords.LoginAsync(client);
        await AddCsrfTokenAsync(client);
        using var denied = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new { command = "Initiate" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var operation = document.GetProperty("paths")
            .GetProperty("/api/v1/disputes/{id}/chargeback-simulations").GetProperty("post");
        Assert.Contains("X-Simulation-Key", operation.GetProperty("description").GetString());
        Assert.Contains("ReceiveRepresentment", operation.GetProperty("description").GetString());
        Assert.True(operation.GetProperty("responses").TryGetProperty("409", out _));
        var schemas = document.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.GetProperty("ChargebackResponse").GetProperty("properties").TryGetProperty("lastOperationOutcome", out _));
        Assert.True(schemas.GetProperty("RepresentmentEvidenceResponse").GetProperty("properties").TryGetProperty("receivedAt", out _));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("not-an-id", "Receipt")]
    [InlineData("00000000-0000-0000-0000-000000000000", "Receipt")]
    [InlineData("aaaaaaaa-1111-4222-8333-444455556666", "UntrustedFile")]
    public async Task Representment_InvalidMetadataReturnsFieldErrors(string? id, string? kind)
    {
        using var client = Client();
        var dispute = await MerchantReview(client);
        await Command(client, dispute, "MerchantContest", "MerchantContested");
        using var response = await client.PostAsJsonAsync(Route(dispute),
            new { command = "ReceiveRepresentment", evidence = id is null ? null : new { id, kind } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("chargeback.invalidRequest", body.GetProperty("code").GetString());
        Assert.StartsWith("/evidence", body.GetProperty("errors")[0].GetProperty("pointer").GetString());
    }

    [Fact]
    public async Task OrdinaryCustomersAndOtherOwners_CannotProcessCases()
    {
        using var simulator = Client();
        var id = await Submit(simulator);
        using var customer = Client(simulator: false);
        using var denied = await customer.PostAsJsonAsync(Route(id), new { command = "Initiate" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        customer.DefaultRequestHeaders.Add("X-Simulation-Key", "incorrect");
        using var wrong = await customer.PostAsJsonAsync(Route(id), new { command = "Initiate" });
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        using var otherApp = _app.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Demo:CustomerId"] = "22222222-2222-2222-2222-222222222222"
            })));
        using var other = otherApp.CreateClient();
        other.DefaultRequestHeaders.Add("X-Simulation-Key", _key);
        await AddCsrfTokenAsync(other);
        using var inaccessible = await other.PostAsJsonAsync(Route(id), new { command = "Initiate" });
        Assert.Equal(HttpStatusCode.NotFound, inaccessible.StatusCode);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Simulation_IsAbsentOutsideDemoEnvironments(string environment)
    {
        using var host = _app.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Simulation-Key", _key);
        using var response = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new { command = "Initiate" });
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.Unauthorized });
    }

    private HttpClient Client(bool simulator = true)
    {
        var client = _app.CreateClient();
        if (simulator) client.DefaultRequestHeaders.Add("X-Simulation-Key", _key);
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "chargeback-simulation-test");
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

    private static async Task<Guid> Submit(HttpClient client, int transaction = 1, string reason = "duplicateCharge")
    {
        using var response = await client.PostAsJsonAsync("/api/v1/disputes", new
        {
            transactionId = $"00000000-0000-0000-0000-{transaction:D12}",
            reasonCode = reason,
            description = "This card purchase appears twice and needs investigation."
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static string Route(Guid id) => $"/api/v1/disputes/{id}/chargeback-simulations";

    private static async Task<Guid> MerchantReview(HttpClient client)
    {
        var id = await Submit(client);
        await Command(client, id, "Initiate", "Initiated");
        await Command(client, id, "ForwardToAcquirer", "MerchantReview");
        return id;
    }

    private static async Task<JsonElement> Command(
        HttpClient client, Guid id, string command, string status, object? evidence = null)
    {
        using var response = await client.PostAsJsonAsync(Route(id), new { command, evidence });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(status, body.GetProperty("chargeback").GetProperty("status").GetString());
        return body;
    }

    private static async Task Conflict(HttpClient client, Guid id, string command)
    {
        using var response = await client.PostAsJsonAsync(Route(id), new { command });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("chargeback.invalidTransition", body.GetProperty("code").GetString());
        Assert.Equal("chargeback-simulation-test", body.GetProperty("correlationId").GetString());
    }

    private sealed class FixedClock : TimeProvider
    {
        public static readonly DateTimeOffset Instant = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Instant;
    }

    private sealed class RecordingGateway : ICardNetworkGateway
    {
        public CardNetworkOutcome Outcome { get; set; } = CardNetworkOutcome.Succeeded;
        public ConcurrentQueue<CardNetworkRequest> Requests { get; } = new();

        public Task<CardNetworkOutcome> ExecuteAsync(CardNetworkRequest request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(request);
            return new MockCardNetworkGateway(Outcome).ExecuteAsync(request, cancellationToken);
        }
    }
}
