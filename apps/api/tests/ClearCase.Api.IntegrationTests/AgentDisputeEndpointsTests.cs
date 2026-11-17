using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClearCase.Application.Chargebacks;
using ClearCase.Application.AgentDisputes;
using ClearCase.Application.DisputeEligibility;
using ClearCase.Application.Disputes;
using ClearCase.Application.Transactions;
using ClearCase.Domain.Chargebacks;
using ClearCase.Domain.DisputeEligibility;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;
using ClearCase.Infrastructure.Chargebacks;
using ClearCase.Infrastructure.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClearCase.Api.IntegrationTests;

public sealed class AgentDisputeEndpointsTests : IClassFixture<SqliteWebApplicationFactory>
{
    private const string CustomerId = "11111111-1111-1111-1111-111111111111";
    private const string OtherCustomerId = "22222222-2222-2222-2222-222222222222";
    private static readonly DateTimeOffset CreatedAt = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
    private readonly SqliteWebApplicationFactory _factory;

    public AgentDisputeEndpointsTests(SqliteWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData(TransactionType.BankCharge, "agent.demo",
        new[] { "StartReview", "Approve", "RequestAdjustment", "ExecuteAdjustment", "Close" })]
    [InlineData(TransactionType.DebitOrder, "agent.demo",
        new[] { "StartReview", "CompleteInvestigation", "RequestReversal", "ExecuteReversal", "Close" })]
    [InlineData(TransactionType.CardPurchase, "supervisor.demo",
        new[] { "Initiate", "ForwardToAcquirer", "MerchantContest", "ReceiveRepresentment",
            "ReviewRepresentment", "Approve", "RequestSettlement", "ExecuteSettlement", "Close" })]
    public async Task EmployeeCanProcessOwnedCaseWithoutCustomerImpersonationOrSimulationKey(
        TransactionType type, string username, string[] commands)
    {
        var caseId = await AddCommandCaseAsync(type);
        using var employee = await CreateSignedInClientAsync(username);
        employee.DefaultRequestHeaders.Add("X-Correlation-ID", "employee-command-correlation");
        long version = 1;
        foreach (var command in commands)
        {
            using var response = await SendCommandAsync(employee, caseId, command, version);
            using var body = await ReadJson(response);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(++version, body.RootElement.GetProperty("version").GetInt64());
            if (command is "Approve" or "RequestReversal")
                Assert.NotEqual("Confirmed", body.RootElement.GetProperty("financialStatus").GetString());
            Assert.DoesNotContain("employeeId", body.RootElement.GetRawText());
        }

        using var owner = await CreateSignedInClientAsync("customer.other");
        using var finalResponse = await owner.GetAsync($"/api/v1/disputes/{caseId}");
        using var final = await ReadJson(finalResponse);
        Assert.Equal(HttpStatusCode.OK, finalResponse.StatusCode);
        Assert.Equal("Closed", final.RootElement.GetProperty("caseStatus").GetString());
        Assert.Equal("Confirmed", final.RootElement.GetProperty("financialStatus").GetString());
        Assert.Equal(version, final.RootElement.GetProperty("timeline").GetArrayLength());
        using var otherCustomer = await CreateSignedInClientAsync("customer.demo");
        using var denied = await otherCustomer.GetAsync($"/api/v1/disputes/{caseId}");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        var employeeId = await database.Users.Where(item => item.UserName == username).Select(item => item.Id).SingleAsync();
        var audit = await database.DisputeCommandAuditEvents.AsNoTracking().Where(item => item.DisputeId == caseId)
            .OrderBy(item => item.Version).ToArrayAsync();
        Assert.Equal(commands, audit.Select(item => item.Command));
        Assert.Equal(Enumerable.Range(2, commands.Length).Select(value => (long)value), audit.Select(item => item.Version));
        Assert.All(audit, item =>
        {
            Assert.Equal(employeeId, item.EmployeeId);
            Assert.Equal("employee-command-correlation", item.CorrelationId);
            Assert.Equal(CreatedAt, item.OccurredAt);
        });
        if (type == TransactionType.CardPurchase)
        {
            var cardAudit = await database.ChargebackAuditEvents.Where(item => item.DisputeId == caseId).ToArrayAsync();
            Assert.All(cardAudit, item => Assert.Equal(employeeId, item.ActorId));
        }
        var outbox = await database.OutboxMessages.Where(item => item.AggregateId == caseId)
            .OrderBy(item => item.AggregateVersion).ToArrayAsync();
        Assert.Equal(audit.Select(item => item.Version), outbox.Select(item => item.AggregateVersion));
        Assert.All(outbox, item => Assert.DoesNotContain(employeeId.ToString(), item.Payload));
        await Assert.ThrowsAsync<SqliteException>(() => database.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"DisputeCommandAuditEvents\" SET \"Command\" = 'changed' WHERE \"DisputeId\" = {caseId}"));
        await Assert.ThrowsAsync<SqliteException>(() => database.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"DisputeCommandAuditEvents\" WHERE \"DisputeId\" = {caseId}"));
    }

    [Theory]
    [InlineData(TransactionType.BankCharge, "StartReview")]
    [InlineData(TransactionType.DebitOrder, "StartReview")]
    [InlineData(TransactionType.CardPurchase, "Initiate")]
    public async Task EmployeeAuditFailureRollsBackCaseTimelineOutboxAndCardAudit(TransactionType type, string command)
    {
        var id = await AddCommandCaseAsync(type);
        using var employee = await CreateSignedInClientAsync("agent.demo");
        employee.DefaultRequestHeaders.Add("X-Correlation-ID", "reject-audit-write");
        await using var scope = _factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        await database.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER "FailTestCommandAudit" BEFORE INSERT ON "DisputeCommandAuditEvents"
            WHEN NEW."CorrelationId" = 'reject-audit-write'
            BEGIN SELECT RAISE(ABORT, 'Test audit write failure'); END;
            """);
        try
        {
            using var response = await SendCommandAsync(employee, id, command, 1);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(1, (await database.Disputes.AsNoTracking().SingleAsync(item => item.Id == id)).Version);
            Assert.Single(await database.DisputeEvents.Where(item => item.DisputeId == id).ToArrayAsync());
            Assert.Empty(await database.DisputeCommandAuditEvents.Where(item => item.DisputeId == id).ToArrayAsync());
            Assert.Empty(await database.OutboxMessages.Where(item => item.AggregateId == id).ToArrayAsync());
            Assert.Empty(await database.ChargebackAuditEvents.Where(item => item.DisputeId == id).ToArrayAsync());
            Assert.Empty(await database.Chargebacks.Where(item => item.DisputeId == id).ToArrayAsync());
        }
        finally
        {
            await database.Database.ExecuteSqlRawAsync("DROP TRIGGER \"FailTestCommandAudit\";");
        }
    }

    [Fact]
    public async Task StaffCommandCannotBypassAnActiveSupervisorEscalation()
    {
        var id = await AddCommandCaseAsync(TransactionType.CardPurchase);
        using var employee = await CreateSignedInClientAsync("agent.demo");
        using var escalation = CreateEscalationRequest(id, "command-escalation-guard",
            "Additional investigation required", "Supervisor review is required before processing this case.");
        using var created = await employee.SendAsync(escalation);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var response = await SendCommandAsync(employee, id, "Initiate", 2);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = await ReadJson(response);
        Assert.Equal("dispute.activeEscalation", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CommandAuditMigrationUpAndDownPreservesExistingCaseAndTimeline()
    {
        var id = await AddCommandCaseAsync(TransactionType.BankCharge);
        using var employee = await CreateSignedInClientAsync("agent.demo");
        using var response = await SendCommandAsync(employee, id, "StartReview", 1);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var source = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        var dispute = await source.Disputes.AsNoTracking().Include(item => item.Events).SingleAsync(item => item.Id == id);
        var transaction = await source.Transactions.AsNoTracking().SingleAsync(item => item.Id == dispute.TransactionId);
        var audit = await source.DisputeCommandAuditEvents.AsNoTracking().SingleAsync(item => item.DisputeId == id);
        await using var isolated = new TemporaryDatabase();
        await using var database = isolated.CreateContext();
        var migrator = database.GetService<IMigrator>();
        const string previous = "20261116151544_AddSupervisorEscalations";
        await migrator.MigrateAsync(previous);
        database.Transactions.Add(transaction);
        database.Disputes.Add(dispute);
        await database.SaveChangesAsync();
        await migrator.MigrateAsync();
        database.DisputeCommandAuditEvents.Add(audit);
        await database.SaveChangesAsync();

        await migrator.MigrateAsync(previous);
        database.ChangeTracker.Clear();
        Assert.Equal(2, (await database.Disputes.SingleAsync(item => item.Id == id)).Version);
        Assert.Equal(2, await database.DisputeEvents.CountAsync(item => item.DisputeId == id));
        Assert.Single(await database.Transactions.ToArrayAsync());
        await migrator.MigrateAsync();
        Assert.Empty(await database.DisputeCommandAuditEvents.ToArrayAsync());
    }

    [Fact]
    public async Task OpenApiDescribesStaffCommandVersionEvidenceAndErrors()
    {
        using var client = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development")).CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJson(response);
        var operation = body.RootElement.GetProperty("paths")
            .GetProperty("/api/v1/agent/disputes/{id}/commands").GetProperty("post");
        foreach (var status in new[] { "200", "400", "401", "403", "404", "409", "503" })
            Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
        var schema = body.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("AgentDisputeCommandRequest");
        Assert.Contains("expectedVersion", schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        Assert.True(schema.GetProperty("properties").TryGetProperty("evidence", out _));
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("customer.demo", HttpStatusCode.Forbidden)]
    public async Task NonEmployeeCannotIssueCommands(string? username, HttpStatusCode expected)
    {
        var caseId = await AddCommandCaseAsync(TransactionType.BankCharge);
        using var client = username is null ? CreateUnauthenticatedClient()
            : await CreateSignedInClientAsync(username);
        using var response = await SendCommandAsync(client, caseId, "StartReview", 1);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task CommandsRequireCsrfAndVersionAndRejectInvalidCommandsWithoutWrites()
    {
        var caseId = await AddCommandCaseAsync(TransactionType.BankCharge);
        using var employee = await CreateSignedInClientAsync("agent.demo");
        employee.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var noCsrf = await SendCommandAsync(employee, caseId, "StartReview", 1);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var csrfBody = await ReadJson(noCsrf);
        Assert.Equal("auth.csrfInvalid", csrfBody.RootElement.GetProperty("code").GetString());
        await SetCsrfTokenAsync(employee);
        foreach (var version in new long?[] { null, 0, -1 })
        {
            using var invalid = await SendCommandAsync(employee, caseId, "StartReview", version);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        foreach (var command in new[] { "Initiate", "0", "startReview" })
        {
            using var invalid = await SendCommandAsync(employee, caseId, command, 1);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var badTransition = await SendCommandAsync(employee, caseId, "ExecuteAdjustment", 1);
        Assert.Equal(HttpStatusCode.Conflict, badTransition.StatusCode);
        using var missing = await SendCommandAsync(employee, Guid.NewGuid(), "StartReview", 1);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var valid = await SendCommandAsync(employee, caseId, "StartReview", 1);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using var stale = await SendCommandAsync(employee, caseId, "Reject", 1);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var staleBody = await ReadJson(stale);
        Assert.Equal("dispute.concurrencyConflict", staleBody.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ConcurrentEmployeeCommandsCommitOneTransition()
    {
        var caseId = await AddCommandCaseAsync(TransactionType.BankCharge);
        using var employee = await CreateSignedInClientAsync("agent.demo");
        var responses = await Task.WhenAll(
            SendCommandAsync(employee, caseId, "StartReview", 1),
            SendCommandAsync(employee, caseId, "StartReview", 1));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
        await using var scope = _factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        Assert.Single(await database.DisputeCommandAuditEvents.Where(item => item.DisputeId == caseId).ToArrayAsync());
        Assert.Single(await database.OutboxMessages.Where(item => item.AggregateId == caseId).ToArrayAsync());
    }

    [Fact]
    public async Task ProductionAllowsStaffDecisionsButNeverUsesMockFinancialConfirmation()
    {
        var rejectedId = await AddCommandCaseAsync(TransactionType.BankCharge);
        using var unconfiguredEmployee = await CreateSignedInClientAsync("agent.demo", "Production");
        using var review = await SendCommandAsync(unconfiguredEmployee, rejectedId, "StartReview", 1);
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        using var noPolicy = await SendCommandAsync(unconfiguredEmployee, rejectedId, "Approve", 2);
        Assert.Equal(HttpStatusCode.Conflict, noPolicy.StatusCode);
        using var rejected = await SendCommandAsync(unconfiguredEmployee, rejectedId, "Reject", 2);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        using var closed = await SendCommandAsync(unconfiguredEmployee, rejectedId, "Close", 3);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);

        var caseId = await AddCommandCaseAsync(TransactionType.BankCharge);
        using var employee = await CreateSignedInClientAsync("agent.demo", "Production", configuredTestPolicy: true);
        long version = 1;
        foreach (var command in new[] { "StartReview", "Approve", "RequestAdjustment" })
        {
            using var response = await SendCommandAsync(employee, caseId, command, version++);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var execute = await SendCommandAsync(employee, caseId, "ExecuteAdjustment", version);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, execute.StatusCode);
        using var body = await ReadJson(execute);
        Assert.Equal("dispute.processingUnavailable", body.RootElement.GetProperty("code").GetString());
        using var stateResponse = await employee.GetAsync($"/api/v1/agent/disputes/{caseId}");
        using var state = await ReadJson(stateResponse);
        Assert.Equal(version, state.RootElement.GetProperty("version").GetInt64());
        Assert.Equal("Requested", state.RootElement.GetProperty("financialStatus").GetString());
        var cardId = await AddCommandCaseAsync(TransactionType.CardPurchase);
        foreach (var command in new[] { "MerchantAccept", "MerchantContest", "ReceiveRepresentment" })
        {
            using var syntheticUpdate = await SendCommandAsync(employee, cardId, command, 1);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, syntheticUpdate.StatusCode);
        }
    }

    private async Task<Guid> AddCommandCaseAsync(TransactionType type)
    {
        var id = await AddDisputeAsync(type, $"DSP-CMD-{Guid.NewGuid():N}", OtherCustomerId);
        await using var scope = _factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        var dispute = await database.Disputes.SingleAsync(item => item.Id == id);
        var transaction = await database.Transactions.SingleAsync(item => item.Id == dispute.TransactionId);
        transaction.IsDebit = true;
        transaction.CollectionDate = DateOnly.FromDateTime(CreatedAt.AddDays(-1).UtcDateTime);
        if (type == TransactionType.DebitOrder) dispute.ReasonCode = "duplicateCollection";
        await database.SaveChangesAsync();
        return id;
    }

    private static Task<HttpResponseMessage> SendCommandAsync(
        HttpClient client, Guid id, string command, long? expectedVersion) =>
        client.PostAsJsonAsync($"/api/v1/agent/disputes/{id}/commands", new
        {
            command, expectedVersion,
            evidence = command == "ReceiveRepresentment"
                ? new { id = Guid.NewGuid(), kind = "Receipt" } : null,
            customerId = CustomerId,
            employeeId = CustomerId
        });

    [Fact]
    public async Task AgentCanSearchAndReadPersistedCasesAcrossCustomersAndCategories()
    {
        var caseId = await AddDisputeAsync(TransactionType.BankCharge, "DSP-AGENT-SEARCH", OtherCustomerId);
        using var agent = await CreateSignedInClientAsync("agent.demo");

        using var listResponse = await agent.GetAsync(
            "/api/v1/agent/disputes?search=DSP-AGENT-SEARCH&pageSize=1");
        using var list = await ReadJson(listResponse);
        var item = Assert.Single(list.RootElement.GetProperty("items").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(caseId, item.GetProperty("id").GetGuid());
        Assert.Equal("BankCharge", item.GetProperty("category").GetString());
        Assert.Equal("CUS-", item.GetProperty("customerReference").GetString()![..4]);
        Assert.Equal(1, item.GetProperty("version").GetInt64());
        Assert.Equal("12345", item.GetProperty("amountMinorUnits").GetString());
        Assert.Equal("2026-11-12T10:00:00+00:00", item.GetProperty("lastActivityAt").GetString());
        Assert.DoesNotContain(OtherCustomerId, list.RootElement.GetRawText());

        using var detailResponse = await agent.GetAsync($"/api/v1/agent/disputes/{caseId}");
        using var detail = await ReadJson(detailResponse);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.Equal("DSP-AGENT-SEARCH", detail.RootElement.GetProperty("reference").GetString());
        Assert.Equal("The customer statement contains no real personal data.",
            detail.RootElement.GetProperty("customerStatement").GetString());
        Assert.Equal("incorrectFee", detail.RootElement.GetProperty("reasonCode").GetString());
        Assert.Equal(1, detail.RootElement.GetProperty("timeline")[0].GetProperty("sequenceNumber").GetInt32());
    }

    [Fact]
    public async Task AgentQueueFiltersBeforeStableCursorPaginationAndRejectsInvalidCursors()
    {
        await AddDisputeAsync(TransactionType.CardPurchase, "DSP-AGENT-PAGE-A");
        await AddDisputeAsync(TransactionType.CardPurchase, "DSP-AGENT-PAGE-B");
        var debitOrderId = await AddDisputeAsync(TransactionType.DebitOrder, "DSP-AGENT-PAGE-C");
        using var agent = await CreateSignedInClientAsync("agent.demo");

        using var allCategoriesResponse = await agent.GetAsync(
            "/api/v1/agent/disputes?search=DSP-AGENT-PAGE&pageSize=10");
        using var allCategories = await ReadJson(allCategoriesResponse);
        Assert.Equal(new[] { "CardPurchase", "CardPurchase", "DebitOrder" },
            allCategories.RootElement.GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("category").GetString()).ToArray());

        using var firstResponse = await agent.GetAsync(
            "/api/v1/agent/disputes?search=DSP-AGENT-PAGE&category=CardPurchase&caseStatus=Submitted&pageSize=1");
        using var first = await ReadJson(firstResponse);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal("DSP-AGENT-PAGE-A", first.RootElement.GetProperty("items")[0]
            .GetProperty("reference").GetString());
        Assert.True(first.RootElement.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean());
        var cursor = first.RootElement.GetProperty("pageInfo").GetProperty("nextCursor").GetString();

        using var nextResponse = await agent.GetAsync(
            $"/api/v1/agent/disputes?search=DSP-AGENT-PAGE&category=CardPurchase&caseStatus=Submitted&pageSize=1&cursor={cursor}");
        using var next = await ReadJson(nextResponse);
        Assert.Equal("DSP-AGENT-PAGE-B", next.RootElement.GetProperty("items")[0]
            .GetProperty("reference").GetString());
        using var debitDetail = await ReadJson(await agent.GetAsync($"/api/v1/agent/disputes/{debitOrderId}"));
        Assert.Equal("EftDebitOrder",
            debitDetail.RootElement.GetProperty("transaction").GetProperty("mandateType").GetString());
        Assert.False(
            debitDetail.RootElement.GetProperty("transaction").GetProperty("mandateAuthorised").GetBoolean());

        using var invalid = await agent.GetAsync("/api/v1/agent/disputes?cursor=not-a-cursor");
        using var invalidBody = await ReadJson(invalid);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("agentDispute.invalidQuery", invalidBody.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AgentNotesAreAppendOnlyIdempotentAndNotInCustomerProjection()
    {
        var caseId = await AddDisputeAsync(TransactionType.CardPurchase, "DSP-AGENT-NOTE");
        using var agent = await CreateSignedInClientAsync("agent.demo");
        using var firstRequest = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/agent/disputes/{caseId}/notes")
        {
            Content = JsonContent.Create(new { text = "Reviewed the synthetic transaction; merchant verification is pending." })
        };
        firstRequest.Headers.Add("Idempotency-Key", "agent-note-retry-1");
        firstRequest.Headers.Add("X-Correlation-ID", "agent-note-correlation");
        using var firstResponse = await agent.SendAsync(firstRequest);
        using var first = await ReadJson(firstResponse);

        using var replayRequest = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/agent/disputes/{caseId}/notes")
        {
            Content = JsonContent.Create(new { text = "Reviewed the synthetic transaction; merchant verification is pending." })
        };
        replayRequest.Headers.Add("Idempotency-Key", "agent-note-retry-1");
        using var replayResponse = await agent.SendAsync(replayRequest);
        using var replay = await ReadJson(replayResponse);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
        Assert.Equal(first.RootElement.GetProperty("id").GetGuid(), replay.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("true", replayResponse.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal("33333333-3333-3333-3333-333333333333",
            first.RootElement.GetProperty("employeeId").GetString());
        Assert.Equal("agent-note-correlation", first.RootElement.GetProperty("correlationId").GetString());
        Assert.NotEqual(Guid.Empty, first.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("2026-11-12T10:00:00+00:00",
            first.RootElement.GetProperty("createdAt").GetString());

        using var changedRequest = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/agent/disputes/{caseId}/notes")
        {
            Content = JsonContent.Create(new { text = "This is different text using the same request key." })
        };
        changedRequest.Headers.Add("Idempotency-Key", "agent-note-retry-1");
        using var changedResponse = await agent.SendAsync(changedRequest);
        using var changed = await ReadJson(changedResponse);
        Assert.Equal(HttpStatusCode.Conflict, changedResponse.StatusCode);
        Assert.Equal("agentNote.idempotencyKeyReused", changed.RootElement.GetProperty("code").GetString());

        using var notesResponse = await agent.GetAsync($"/api/v1/agent/disputes/{caseId}/notes");
        using var notes = await ReadJson(notesResponse);
        Assert.Single(notes.RootElement.EnumerateArray());
        using var reopenedAgent = await CreateSignedInClientAsync("agent.demo");
        using var reopenedNotes = await ReadJson(await reopenedAgent.GetAsync(
            $"/api/v1/agent/disputes/{caseId}/notes"));
        Assert.Single(reopenedNotes.RootElement.EnumerateArray());
        var noteId = first.RootElement.GetProperty("id").GetGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
            await Assert.ThrowsAsync<SqliteException>(() => database.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"InvestigationNotes\" SET \"Text\" = 'changed' WHERE \"Id\" = {noteId}"));
            var publicDescriptions = await database.DisputeEvents.AsNoTracking()
                .Select(item => item.PublicDescription).ToArrayAsync();
            var outboxPayloads = await database.OutboxMessages.AsNoTracking()
                .Select(item => item.Payload).ToArrayAsync();
            Assert.DoesNotContain(publicDescriptions, value => value.Contains("merchant verification is pending",
                StringComparison.Ordinal));
            Assert.DoesNotContain(outboxPayloads, value => value.Contains("merchant verification is pending",
                StringComparison.Ordinal));
        }

        using var customer = await CreateSignedInClientAsync("customer.demo");
        using var customerResponse = await customer.GetAsync($"/api/v1/disputes/{caseId}");
        using var customerBody = await ReadJson(customerResponse);
        Assert.Equal(HttpStatusCode.OK, customerResponse.StatusCode);
        Assert.DoesNotContain("merchant verification is pending", customerBody.RootElement.GetRawText());
        using var forbiddenNotes = await customer.GetAsync($"/api/v1/agent/disputes/{caseId}/notes");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenNotes.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("customer.demo", HttpStatusCode.Forbidden)]
    public async Task NonAgentCannotAccessAgentQueue(string? username, HttpStatusCode expected)
    {
        using var client = username is null
            ? CreateUnauthenticatedClient()
            : await CreateSignedInClientAsync(username);
        using var response = await client.GetAsync("/api/v1/agent/disputes");
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task NoteCreationRequiresCsrfAndRejectsInvalidIdsAndShortText()
    {
        using var agent = await CreateSignedInClientAsync("agent.demo");
        agent.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var csrfResponse = await agent.PostAsJsonAsync(
            $"/api/v1/agent/disputes/{Guid.NewGuid()}/notes",
            new { text = "This note is valid and sufficiently long." });
        Assert.Equal(HttpStatusCode.BadRequest, csrfResponse.StatusCode);

        using var invalidId = await agent.GetAsync("/api/v1/agent/disputes/not-a-guid");
        using var invalidIdBody = await ReadJson(invalidId);
        Assert.Equal(HttpStatusCode.BadRequest, invalidId.StatusCode);
        Assert.Equal("agentDispute.invalidId", invalidIdBody.RootElement.GetProperty("code").GetString());

        await SetCsrfTokenAsync(agent);
        using var shortNoteRequest = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/agent/disputes/{Guid.NewGuid()}/notes")
        {
            Content = JsonContent.Create(new { text = "too short" })
        };
        shortNoteRequest.Headers.Add("Idempotency-Key", "short-note-test");
        using var shortNote = await agent.SendAsync(shortNoteRequest);
        using var shortNoteBody = await ReadJson(shortNote);
        Assert.Equal(HttpStatusCode.BadRequest, shortNote.StatusCode);
        Assert.Equal("agentNote.length", shortNoteBody.RootElement.GetProperty("errors")[0]
            .GetProperty("code").GetString());
    }

    [Fact]
    public async Task AgentEscalationIsIdempotentAndPublishesOnlySafeProgress()
    {
        var caseId = await AddDisputeAsync(TransactionType.CardPurchase, "DSP-ESCALATION-CREATE");
        const string privateNote = "Standard checks were inconclusive; specialist review is required.";
        const string idempotencyKey = "agent-escalation-create-1";
        using var agent = await CreateSignedInClientAsync("agent.demo");

        using var firstResponse = await agent.SendAsync(CreateEscalationRequest(
            caseId, idempotencyKey, "Additional investigation required", privateNote));
        using var first = await ReadJson(firstResponse);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var escalationId = first.RootElement.GetProperty("id").GetGuid();
        Assert.False(string.IsNullOrWhiteSpace(firstResponse.Headers.Location?.ToString()));
        Assert.Equal("Open", first.RootElement.GetProperty("status").GetString());
        Assert.Equal(privateNote, first.RootElement.GetProperty("note").GetString());
        Assert.False(first.RootElement.TryGetProperty("history", out _));
        Assert.False(first.RootElement.TryGetProperty("createdByEmployeeId", out _));

        using var agentCaseResponse = await agent.GetAsync($"/api/v1/agent/disputes/{caseId}");
        using var agentCase = await ReadJson(agentCaseResponse);
        Assert.Equal(escalationId,
            agentCase.RootElement.GetProperty("activeEscalation").GetProperty("id").GetGuid());

        using var replayResponse = await agent.SendAsync(CreateEscalationRequest(
            caseId, idempotencyKey, "Additional investigation required", privateNote));
        using var replay = await ReadJson(replayResponse);
        Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
        Assert.Equal(escalationId, replay.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("true", replayResponse.Headers.GetValues("Idempotent-Replayed").Single());

        using var changedResponse = await agent.SendAsync(CreateEscalationRequest(
            caseId, idempotencyKey, "Additional investigation required",
            "The same key cannot carry a different internal investigation note."));
        Assert.Equal(HttpStatusCode.Conflict, changedResponse.StatusCode);

        using var duplicateResponse = await agent.SendAsync(CreateEscalationRequest(
            caseId, "agent-escalation-create-2", "Additional investigation required",
            "A second active escalation must not be created for this case."));
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
            var dispute = await database.Disputes.SingleAsync(item => item.Id == caseId);
            Assert.Equal(DisputeCaseStatus.ManualReview, dispute.CaseStatus);
            Assert.Equal(DisputeDecisionStatus.Pending, dispute.DecisionStatus);
            Assert.Equal(DisputeFinancialStatus.NotInitiated, dispute.FinancialStatus);
            Assert.Equal(2, dispute.Version);
            var publicEvent = await database.DisputeEvents.SingleAsync(
                item => item.DisputeId == caseId && item.SequenceNumber == 2);
            Assert.Equal("SpecialistReview", publicEvent.EventType);
            Assert.Contains("additional review", publicEvent.PublicDescription, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(privateNote, publicEvent.PublicDescription, StringComparison.Ordinal);
            var outboxPayload = await database.OutboxMessages
                .Where(item => item.AggregateId == caseId)
                .Select(item => item.Payload).SingleAsync();
            Assert.DoesNotContain(privateNote, outboxPayload, StringComparison.Ordinal);
        }

        using var customer = await CreateSignedInClientAsync("customer.demo");
        using var customerResponse = await customer.GetAsync($"/api/v1/disputes/{caseId}");
        using var customerBody = await ReadJson(customerResponse);
        Assert.Equal(HttpStatusCode.OK, customerResponse.StatusCode);
        Assert.Equal("ManualReview", customerBody.RootElement.GetProperty("caseStatus").GetString());
        Assert.Contains("additional review", customerBody.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(privateNote, customerBody.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SupervisorMustAcknowledgeBeforeResolvingAndCannotChangeFinancialStatus()
    {
        var caseId = await AddDisputeAsync(TransactionType.CardPurchase, "DSP-ESCALATION-REVIEW");
        const string privateNote = "The initial review needs an independent supervisor assessment.";
        const string resolutionNote = "Additional checks are complete; the investigation may continue.";
        using var agent = await CreateSignedInClientAsync("agent.demo");
        using var createResponse = await agent.SendAsync(CreateEscalationRequest(
            caseId, "agent-escalation-review-1", "Policy exception review", privateNote));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        using var created = await ReadJson(createResponse);
        var escalationId = created.RootElement.GetProperty("id").GetGuid();

        using var agentQueueDenied = await agent.GetAsync("/api/v1/supervisor/escalations");
        Assert.Equal(HttpStatusCode.Forbidden, agentQueueDenied.StatusCode);
        using var customer = await CreateSignedInClientAsync("customer.demo");
        using var customerQueueDenied = await customer.GetAsync("/api/v1/supervisor/escalations");
        Assert.Equal(HttpStatusCode.Forbidden, customerQueueDenied.StatusCode);
        using var anonymous = CreateUnauthenticatedClient();
        using var anonymousQueueDenied = await anonymous.GetAsync("/api/v1/supervisor/escalations");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousQueueDenied.StatusCode);

        using var supervisor = await CreateSignedInClientAsync("supervisor.demo");
        using var queueResponse = await supervisor.GetAsync(
            "/api/v1/supervisor/escalations?search=DSP-ESCALATION-REVIEW&pageSize=10");
        using var queue = await ReadJson(queueResponse);
        Assert.Equal(HttpStatusCode.OK, queueResponse.StatusCode);
        Assert.Equal(escalationId, Assert.Single(queue.RootElement.GetProperty("items").EnumerateArray())
            .GetProperty("id").GetGuid());

        using var supervisorDetailResponse = await supervisor.GetAsync(
            $"/api/v1/supervisor/escalations/{escalationId}");
        using var supervisorDetail = await ReadJson(supervisorDetailResponse);
        Assert.Equal(HttpStatusCode.OK, supervisorDetailResponse.StatusCode);
        Assert.Equal(privateNote, supervisorDetail.RootElement.GetProperty("note").GetString());

        using var prematureResolution = await supervisor.SendAsync(CreateResolutionRequest(
            escalationId, "supervisor-escalation-resolve-early",
            "The supervisor must acknowledge before recording a resolution."));
        Assert.Equal(HttpStatusCode.Conflict, prematureResolution.StatusCode);

        using var acknowledgement = CreateAcknowledgementRequest(escalationId, "supervisor-escalation-ack-1");
        using var acknowledgementResponse = await supervisor.SendAsync(acknowledgement);
        using var acknowledged = await ReadJson(acknowledgementResponse);
        Assert.True(acknowledgementResponse.StatusCode == HttpStatusCode.OK,
            await acknowledgementResponse.Content.ReadAsStringAsync());
        Assert.Equal("Acknowledged", acknowledged.RootElement.GetProperty("status").GetString());
        Assert.Equal("33333333-3333-3333-3333-333333333334",
            acknowledged.RootElement.GetProperty("history")[1].GetProperty("employeeId").GetString());
        using var acknowledgementReplay = CreateAcknowledgementRequest(escalationId, "supervisor-escalation-ack-1");
        using var acknowledgementReplayResponse = await supervisor.SendAsync(acknowledgementReplay);
        Assert.Equal(HttpStatusCode.OK, acknowledgementReplayResponse.StatusCode);
        Assert.Equal("true", acknowledgementReplayResponse.Headers.GetValues("Idempotent-Replayed").Single());

        using var resolutionResponse = await supervisor.SendAsync(CreateResolutionRequest(
            escalationId, "supervisor-escalation-resolve-1", resolutionNote));
        using var resolved = await ReadJson(resolutionResponse);
        Assert.Equal(HttpStatusCode.OK, resolutionResponse.StatusCode);
        Assert.Equal("Resolved", resolved.RootElement.GetProperty("status").GetString());
        Assert.Equal(resolutionNote, resolved.RootElement.GetProperty("history")[2]
            .GetProperty("privateNote").GetString());
        Assert.Equal("33333333-3333-3333-3333-333333333334",
            resolved.RootElement.GetProperty("history")[2].GetProperty("employeeId").GetString());

        // Replaying the Agent's original key after resolution must not reveal any
        // Supervisor-only notes or audit details, even though replay returns current status.
        using var agentReplayResponse = await agent.SendAsync(CreateEscalationRequest(
            caseId, "agent-escalation-review-1", "Policy exception review", privateNote));
        using var agentReplay = await ReadJson(agentReplayResponse);
        Assert.Equal(HttpStatusCode.Created, agentReplayResponse.StatusCode);
        Assert.Equal("true", agentReplayResponse.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(escalationId, agentReplay.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("Resolved", agentReplay.RootElement.GetProperty("status").GetString());
        Assert.Equal(privateNote, agentReplay.RootElement.GetProperty("note").GetString());
        Assert.False(agentReplay.RootElement.TryGetProperty("history", out _));
        Assert.False(agentReplay.RootElement.TryGetProperty("createdByEmployeeId", out _));
        Assert.False(agentReplay.RootElement.TryGetProperty("financialStatus", out _));
        Assert.DoesNotContain(resolutionNote, agentReplay.RootElement.GetRawText(), StringComparison.Ordinal);
        using var agentSupervisorDetail = await agent.GetAsync(
            $"/api/v1/supervisor/escalations/{escalationId}");
        Assert.Equal(HttpStatusCode.Forbidden, agentSupervisorDetail.StatusCode);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
            var dispute = await database.Disputes.SingleAsync(item => item.Id == caseId);
            Assert.Equal(DisputeCaseStatus.UnderReview, dispute.CaseStatus);
            Assert.Equal(DisputeDecisionStatus.Pending, dispute.DecisionStatus);
            Assert.Equal(DisputeFinancialStatus.NotInitiated, dispute.FinancialStatus);
            Assert.Equal(3, dispute.Version);
            var timeline = await database.DisputeEvents.AsNoTracking()
                .Where(item => item.DisputeId == caseId)
                .OrderBy(item => item.SequenceNumber).ToArrayAsync();
            Assert.Equal(3, timeline.Length);
            Assert.Equal("SpecialistReviewComplete", timeline[2].EventType);
            Assert.DoesNotContain(privateNote, timeline[2].PublicDescription, StringComparison.Ordinal);
            Assert.DoesNotContain(resolutionNote, timeline[2].PublicDescription, StringComparison.Ordinal);
            var outboxPayloads = await database.OutboxMessages.AsNoTracking()
                .Where(item => item.AggregateId == caseId)
                .Select(item => item.Payload).ToArrayAsync();
            Assert.Equal(2, outboxPayloads.Length);
            Assert.DoesNotContain(outboxPayloads, value => value.Contains(privateNote, StringComparison.Ordinal));
            Assert.DoesNotContain(outboxPayloads, value => value.Contains(resolutionNote, StringComparison.Ordinal));
            var lastAuditEventId = resolved.RootElement.GetProperty("history")[2]
                .GetProperty("id").GetGuid();
            await Assert.ThrowsAsync<SqliteException>(() => database.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"DisputeEscalationAuditEvents\" SET \"PrivateNote\" = 'changed' WHERE \"Id\" = {lastAuditEventId}"));
            await Assert.ThrowsAsync<SqliteException>(() => database.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM \"DisputeEscalationAuditEvents\" WHERE \"Id\" = {lastAuditEventId}"));
        }

        using var customerCaseResponse = await customer.GetAsync($"/api/v1/disputes/{caseId}");
        using var customerCase = await ReadJson(customerCaseResponse);
        Assert.Equal("UnderReview", customerCase.RootElement.GetProperty("caseStatus").GetString());
        Assert.Equal("Pending", customerCase.RootElement.GetProperty("decisionStatus").GetString());
        Assert.Equal("NotInitiated", customerCase.RootElement.GetProperty("financialStatus").GetString());
        Assert.Contains("Additional review is complete", customerCase.RootElement.GetRawText(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(privateNote, customerCase.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(resolutionNote, customerCase.RootElement.GetRawText(), StringComparison.Ordinal);
    }


    [Theory]
    [InlineData(DisputeCaseStatus.Closed, DisputeDecisionStatus.Rejected, DisputeFinancialStatus.NotInitiated)]
    [InlineData(DisputeCaseStatus.UnderReview, DisputeDecisionStatus.Pending, DisputeFinancialStatus.NotInitiated)]
    [InlineData(DisputeCaseStatus.ManualReview, DisputeDecisionStatus.Approved, DisputeFinancialStatus.NotInitiated)]
    [InlineData(DisputeCaseStatus.ManualReview, DisputeDecisionStatus.Pending, DisputeFinancialStatus.Confirmed)]
    public async Task SupervisorResolutionCannotReopenOrOverwriteAChangedCase(
        DisputeCaseStatus caseStatus, DisputeDecisionStatus decisionStatus,
        DisputeFinancialStatus financialStatus)
    {
        var caseId = await AddDisputeAsync(TransactionType.CardPurchase, "DSP-STATE-" + Guid.NewGuid().ToString("N"));
        using var agent = await CreateSignedInClientAsync("agent.demo");
        using var createdResponse = await agent.SendAsync(CreateEscalationRequest(
            caseId, "state-escalation-create", "Additional investigation required",
            "A supervisor must independently review this synthetic case."));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        using var created = await ReadJson(createdResponse);
        var escalationId = created.RootElement.GetProperty("id").GetGuid();

        using var supervisor = await CreateSignedInClientAsync("supervisor.demo");
        using var acknowledgeResponse = await supervisor.SendAsync(CreateAcknowledgementRequest(
            escalationId, "state-escalation-ack"));
        Assert.Equal(HttpStatusCode.OK, acknowledgeResponse.StatusCode);

        long storedVersion;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
            var dispute = await db.Disputes.SingleAsync(item => item.Id == caseId);
            // Models a concurrent legacy/simulation path bypassing staff command guards.
            dispute.CaseStatus = caseStatus;
            dispute.DecisionStatus = decisionStatus;
            dispute.FinancialStatus = financialStatus;
            dispute.Version++;
            storedVersion = dispute.Version;
            await db.SaveChangesAsync();
        }

        using var resolutionResponse = await supervisor.SendAsync(CreateResolutionRequest(
            escalationId, "state-escalation-resolve",
            "This synthetic case has already transitioned and must not be reopened."));
        using var error = await ReadJson(resolutionResponse);
        Assert.Equal(HttpStatusCode.Conflict, resolutionResponse.StatusCode);
        Assert.Equal("supervisorEscalation.invalidTransition",
            error.RootElement.GetProperty("code").GetString());

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        var savedDispute = await verification.Disputes.AsNoTracking().SingleAsync(item => item.Id == caseId);
        Assert.Equal(caseStatus, savedDispute.CaseStatus);
        Assert.Equal(decisionStatus, savedDispute.DecisionStatus);
        Assert.Equal(financialStatus, savedDispute.FinancialStatus);
        Assert.Equal(storedVersion, savedDispute.Version);
        var savedEscalation = await verification.DisputeEscalations.AsNoTracking()
            .SingleAsync(item => item.Id == escalationId);
        Assert.Equal(DisputeEscalationStatus.Acknowledged, savedEscalation.Status);
        Assert.Equal(2, await verification.DisputeEscalationAuditEvents
            .CountAsync(item => item.EscalationId == escalationId));
        Assert.Equal(2, await verification.DisputeEvents.CountAsync(item => item.DisputeId == caseId));
        Assert.Equal(1, await verification.OutboxMessages.CountAsync(item => item.AggregateId == caseId));
    }


    [Theory]
    [InlineData(DisputeCaseStatus.Closed, DisputeDecisionStatus.Rejected, DisputeFinancialStatus.NotInitiated)]
    [InlineData(DisputeCaseStatus.UnderReview, DisputeDecisionStatus.Pending, DisputeFinancialStatus.NotInitiated)]
    [InlineData(DisputeCaseStatus.ManualReview, DisputeDecisionStatus.Approved, DisputeFinancialStatus.NotInitiated)]
    [InlineData(DisputeCaseStatus.ManualReview, DisputeDecisionStatus.Pending, DisputeFinancialStatus.Confirmed)]
    public async Task SupervisorResolutionCannotOverwriteAnInterveningDisputeDecision(
        DisputeCaseStatus caseStatus, DisputeDecisionStatus decisionStatus,
        DisputeFinancialStatus financialStatus)
    {
        var caseId = await AddDisputeAsync(TransactionType.CardPurchase,
            "DSP-STATE-" + Guid.NewGuid().ToString("N"));
        using var agent = await CreateSignedInClientAsync("agent.demo");
        using var createdResponse = await agent.SendAsync(CreateEscalationRequest(
            caseId, "state-escalation-create", "Additional investigation required",
            "An independent supervisor review is required for this case."));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        using var created = await ReadJson(createdResponse);
        var escalationId = created.RootElement.GetProperty("id").GetGuid();

        using var supervisor = await CreateSignedInClientAsync("supervisor.demo");
        using var acknowledgedResponse = await supervisor.SendAsync(CreateAcknowledgementRequest(
            escalationId, "state-escalation-ack"));
        Assert.Equal(HttpStatusCode.OK, acknowledgedResponse.StatusCode);

        long expectedVersion;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
            var caseRow = await db.Disputes.SingleAsync(item => item.Id == caseId);
            // Simulates a legacy/simulation path writing while an escalation remains active.
            caseRow.CaseStatus = caseStatus;
            caseRow.DecisionStatus = decisionStatus;
            caseRow.FinancialStatus = financialStatus;
            caseRow.Version++;
            expectedVersion = caseRow.Version;
            await db.SaveChangesAsync();
        }

        using var response = await supervisor.SendAsync(CreateResolutionRequest(
            escalationId, "state-escalation-resolve",
            "This case moved to a new state before specialist review finished."));
        using var body = await ReadJson(response);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("supervisorEscalation.invalidTransition", body.RootElement.GetProperty("code").GetString());

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        var saved = await verification.Disputes.AsNoTracking().SingleAsync(item => item.Id == caseId);
        Assert.Equal(caseStatus, saved.CaseStatus);
        Assert.Equal(decisionStatus, saved.DecisionStatus);
        Assert.Equal(financialStatus, saved.FinancialStatus);
        Assert.Equal(expectedVersion, saved.Version);
        var escalation = await verification.DisputeEscalations.AsNoTracking()
            .SingleAsync(item => item.Id == escalationId);
        Assert.Equal(DisputeEscalationStatus.Acknowledged, escalation.Status);
        Assert.Equal(2, await verification.DisputeEscalationAuditEvents
            .CountAsync(item => item.EscalationId == escalationId));
        Assert.Equal(2, await verification.DisputeEvents.CountAsync(item => item.DisputeId == caseId));
        Assert.Equal(1, await verification.OutboxMessages.CountAsync(item => item.AggregateId == caseId));
    }


    [Theory]
    [InlineData(TransactionType.DebitOrder)]
    [InlineData(TransactionType.BankCharge)]
    [InlineData(TransactionType.CardPurchase)]
    public async Task AnActiveEscalationBlocksAllPersistedProcessingPaths(TransactionType category)
    {
        var caseId = await AddDisputeAsync(category,
            "DSP-ESC-BLOCK-" + Guid.NewGuid().ToString("N"));
        using var agent = await CreateSignedInClientAsync("agent.demo");
        using var creation = await agent.SendAsync(CreateEscalationRequest(
            caseId, "block-processing-" + Guid.NewGuid().ToString("N"),
            "Additional investigation required",
            "Do not process this case until the independent Supervisor review is complete."));
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        var disputes = new SqliteDisputeStore(db);
        var customerId = Guid.Parse(CustomerId);
        var callbackInvoked = false;

        async Task<DebitOrderTransition> DebitTransition(DisputeView _) {
            callbackInvoked = true;
            throw new InvalidOperationException("The transition must not be invoked.");
        }
        async Task<BankChargeTransition> BankTransition(DisputeView _) {
            callbackInvoked = true;
            throw new InvalidOperationException("The transition must not be invoked.");
        }
        async Task<ChargebackTransition> CardTransition(DisputeView _) {
            callbackInvoked = true;
            throw new InvalidOperationException("The transition must not be invoked.");
        }

        switch (category)
        {
            case TransactionType.DebitOrder:
                await Assert.ThrowsAsync<ActiveDisputeEscalationException>(() =>
                    disputes.ApplyAsync(customerId, caseId, DebitOrderCommand.StartReview,
                        DebitTransition, CreatedAt, CancellationToken.None, expectedVersion: 2));
                break;
            case TransactionType.BankCharge:
                var bank = new SqliteBankChargeProcessingStore(db, disputes);
                await Assert.ThrowsAsync<ActiveDisputeEscalationException>(() =>
                    bank.ApplyAsync(customerId, caseId, BankChargeCommand.StartReview,
                        CreatedAt, BankTransition, CancellationToken.None, expectedVersion: 2));
                break;
            case TransactionType.CardPurchase:
                var card = new SqliteChargebackProcessingStore(db, disputes);
                await Assert.ThrowsAsync<ActiveDisputeEscalationException>(() =>
                    card.ApplyAsync(customerId, new ChargebackProcessingCommand(
                            caseId, ChargebackCommand.Initiate, null, null,
                            "escalation-block-test", 2),
                        CreatedAt, CardTransition, CancellationToken.None));
                break;
        }
        Assert.False(callbackInvoked);
        var unchanged = await db.Disputes.AsNoTracking().SingleAsync(item => item.Id == caseId);
        Assert.Equal(DisputeCaseStatus.ManualReview, unchanged.CaseStatus);
        Assert.Equal(2, unchanged.Version);
        Assert.Equal(2, await db.DisputeEvents.CountAsync(item => item.DisputeId == caseId));
        Assert.Single(await db.OutboxMessages.Where(item => item.AggregateId == caseId).ToArrayAsync());
    }

    [Fact]
    public async Task ConcurrentSupervisorResolutionsCommitExactlyOnce()
    {
        var caseId = await AddDisputeAsync(TransactionType.CardPurchase,
            "DSP-RESOLUTION-RACE-" + Guid.NewGuid().ToString("N"));
        using var agent = await CreateSignedInClientAsync("agent.demo");
        using var createdResponse = await agent.SendAsync(CreateEscalationRequest(
            caseId, "resolution-race-created", "Additional investigation required",
            "Supervisor review must finish before the investigation can continue."));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        using var created = await ReadJson(createdResponse);
        var escalationId = created.RootElement.GetProperty("id").GetGuid();
        using var supervisor = await CreateSignedInClientAsync("supervisor.demo");
        using var ack = await supervisor.SendAsync(CreateAcknowledgementRequest(
            escalationId, "resolution-race-ack"));
        Assert.Equal(HttpStatusCode.OK, ack.StatusCode);

        using var requestA = CreateResolutionRequest(escalationId, "resolution-race-a",
            "I have reviewed this case and approved further investigation.");
        using var requestB = CreateResolutionRequest(escalationId, "resolution-race-b",
            "Independent reviewer has completed this second request.");
        var outcomes = await Task.WhenAll(supervisor.SendAsync(requestA), supervisor.SendAsync(requestB));
        try
        {
            Assert.Single(outcomes, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(outcomes, response => response.StatusCode == HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (var response in outcomes) response.Dispose();
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        var dispute = await db.Disputes.AsNoTracking().SingleAsync(item => item.Id == caseId);
        Assert.Equal(DisputeCaseStatus.UnderReview, dispute.CaseStatus);
        Assert.Equal(3, dispute.Version);
        Assert.Equal(3, await db.DisputeEscalationAuditEvents.CountAsync(item => item.EscalationId == escalationId));
        Assert.Equal(3, await db.DisputeEvents.CountAsync(item => item.DisputeId == caseId));
        Assert.Equal(2, await db.OutboxMessages.CountAsync(item => item.AggregateId == caseId));
    }

    [Fact]
    public async Task ConcurrentLegacyClosureAndSupervisorResolutionNeverReopenTheClosedCase()
    {
        var caseId = await AddDisputeAsync(TransactionType.CardPurchase,
            "DSP-CLOSE-RACE-" + Guid.NewGuid().ToString("N"));
        using var agent = await CreateSignedInClientAsync("agent.demo");
        using var createdResponse = await agent.SendAsync(CreateEscalationRequest(
            caseId, "close-race-created", "Policy exception review",
            "Independent assessment is needed before continuing this investigation."));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        using var created = await ReadJson(createdResponse);
        var escalationId = created.RootElement.GetProperty("id").GetGuid();
        using var supervisor = await CreateSignedInClientAsync("supervisor.demo");
        using var acknowledged = await supervisor.SendAsync(CreateAcknowledgementRequest(
            escalationId, "close-race-ack"));
        Assert.Equal(HttpStatusCode.OK, acknowledged.StatusCode);

        async Task CloseFromLegacyWriterAsync()
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
            // Models the old local simulation path committing independently of the
            // Supervisor endpoint, including its dispute version increment.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Disputes" SET "CaseStatus" = 'Closed',
                  "DecisionStatus" = 'Rejected', "Version" = "Version" + 1
                WHERE "Id" = {caseId}
                """);
        }

        using var resolutionRequest = CreateResolutionRequest(
            escalationId, "close-race-resolve",
            "The investigation was independently reviewed and can resume if still eligible.");
        var resolutionTask = supervisor.SendAsync(resolutionRequest);
        await Task.WhenAll(resolutionTask, CloseFromLegacyWriterAsync());
        using var resolution = await resolutionTask;
        Assert.Contains(resolution.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var db2 = verificationScope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        var dispute = await db2.Disputes.AsNoTracking().SingleAsync(item => item.Id == caseId);
        Assert.Equal(DisputeCaseStatus.Closed, dispute.CaseStatus);
        Assert.Equal(DisputeDecisionStatus.Rejected, dispute.DecisionStatus);
        var escalation = await db2.DisputeEscalations.AsNoTracking()
            .SingleAsync(item => item.Id == escalationId);
        var resolved = resolution.StatusCode == HttpStatusCode.OK;
        Assert.Equal(resolved ? DisputeEscalationStatus.Resolved : DisputeEscalationStatus.Acknowledged,
            escalation.Status);
        Assert.Equal(resolved ? 3 : 2,
            await db2.DisputeEscalationAuditEvents.CountAsync(item => item.EscalationId == escalationId));
        Assert.Equal(resolved ? 3 : 2,
            await db2.DisputeEvents.CountAsync(item => item.DisputeId == caseId));
        Assert.Equal(resolved ? 2 : 1,
            await db2.OutboxMessages.CountAsync(item => item.AggregateId == caseId));
    }

    private static HttpRequestMessage CreateEscalationRequest(
        Guid caseId, string idempotencyKey, string reason, string note)
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/agent/disputes/{caseId}/escalations")
        {
            Content = JsonContent.Create(new { reason, note })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static HttpRequestMessage CreateAcknowledgementRequest(Guid escalationId, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/supervisor/escalations/{escalationId}/acknowledgements");
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static HttpRequestMessage CreateResolutionRequest(Guid escalationId, string idempotencyKey, string note)
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/supervisor/escalations/{escalationId}/resolutions")
        {
            Content = JsonContent.Create(new { note })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private async Task<Guid> AddDisputeAsync(
        TransactionType type, string reference, string customerId = CustomerId)
    {
        var transactionId = Guid.NewGuid();
        var disputeId = Guid.NewGuid();
        await using var scope = _factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        database.Transactions.Add(new PersistedTransaction
        {
            Id = transactionId,
            CustomerId = Guid.Parse(customerId),
            Type = type,
            Description = "Synthetic operational investigation transaction",
            AmountMinorUnits = 12345,
            Currency = "ZAR",
            TransactionDate = CreatedAt.AddDays(-1),
            Status = TransactionStatus.Posted,
            IsDisputable = true,
            IsDebit = false,
            HasRequiredDisputeInformation = true,
            MandateType = type == TransactionType.DebitOrder ? DebitOrderMandateType.EftDebitOrder : null,
            CollectionDate = type == TransactionType.DebitOrder ? DateOnly.FromDateTime(CreatedAt.UtcDateTime) : null,
            MandateAuthorised = type == TransactionType.DebitOrder ? false : null,
            MandateMatchesCollection = type == TransactionType.DebitOrder ? false : null
        });
        database.Disputes.Add(new PersistedDispute
        {
            Id = disputeId,
            Reference = reference,
            CustomerId = Guid.Parse(customerId),
            TransactionId = transactionId,
            Category = (TransactionCategory)type,
            ReasonCode = type == TransactionType.BankCharge ? "incorrectFee" : "unrecognisedTransaction",
            Description = "The customer statement contains no real personal data.",
            CaseStatus = DisputeCaseStatus.Submitted,
            DecisionStatus = DisputeDecisionStatus.Pending,
            FinancialStatus = DisputeFinancialStatus.NotInitiated,
            EligibilityOutcome = DisputeEligibilityOutcome.EligibleToSubmit,
            EligibilityPolicyVersion = "synthetic-test-v1",
            Version = 1,
            CreatedAt = CreatedAt,
            Events =
            [
                new PersistedDisputeEvent
                {
                    Id = Guid.NewGuid(),
                    DisputeId = disputeId,
                    SequenceNumber = 1,
                    EventType = "DisputeSubmitted",
                    CaseStatus = DisputeCaseStatus.Submitted,
                    PublicDescription = "Your dispute was received and is awaiting review.",
                    OccurredAt = CreatedAt
                }
            ]
        });
        await database.SaveChangesAsync();
        return disputeId;
    }

    private HttpClient CreateUnauthenticatedClient() =>
        _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production")).CreateClient();

    private async Task<HttpClient> CreateSignedInClientAsync(
        string username, string environment = "Testing", bool configuredTestPolicy = false)
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FrozenTimeProvider(CreatedAt));
                if (configuredTestPolicy)
                {
                    services.RemoveAll<DisputeEligibilityPolicySettings>();
                    services.AddSingleton(new DisputeEligibilityPolicySettings { DemoPoliciesEnabled = true });
                }
                if (environment == "Testing")
                {
                    services.RemoveAll<IDebitOrderMandateReader>();
                    services.AddSingleton<IDebitOrderMandateReader>(new CommandMandateReader());
                }
            });
        }).CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { username, password = TestIdentityPasswords.For(username) });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        await SetCsrfTokenAsync(client);
        return client;
    }

    private static async Task SetCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/auth/me");
        using var body = await ReadJson(response);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", body.RootElement.GetProperty("csrfToken").GetString());
    }

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private sealed class FrozenTimeProvider(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }

    private sealed class CommandMandateReader : IDebitOrderMandateReader
    {
        public Task<DebitOrderMandate?> ReadAsync(
            Guid customerId, Guid transactionId, CancellationToken cancellationToken) =>
            Task.FromResult<DebitOrderMandate?>(new(DebitOrderMandateType.EftDebitOrder,
                DebitOrderMandateStatus.Active, true, 123.45m, "ZAR",
                DateOnly.FromDateTime(CreatedAt.AddDays(-1).UtcDateTime)));
    }
}
