using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClearCase.Application.Disputes;
using ClearCase.Application.Transactions;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;
using ClearCase.Infrastructure.Disputes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Api.IntegrationTests;

public sealed class AgentSlaEndpointsTests : IClassFixture<SqliteWebApplicationFactory>
{
    private readonly SqliteWebApplicationFactory _factory;
    private readonly UxApp _app;

    public AgentSlaEndpointsTests(SqliteWebApplicationFactory factory)
    {
        _factory = factory;
        _app = new UxApp(factory);
    }

    [Theory]
    [InlineData(60, DisputeCaseStatus.Submitted, "OnTrack")]
    [InlineData(95 * 3600, DisputeCaseStatus.UnderReview, "OnTrack")]
    [InlineData(96 * 3600, DisputeCaseStatus.UnderReview, "DueSoon")]
    [InlineData(120 * 3600, DisputeCaseStatus.ManualReview, "DueSoon")]
    [InlineData(120 * 3600 + 1, DisputeCaseStatus.ManualReview, "Breached")]
    [InlineData(400 * 3600, DisputeCaseStatus.Closed, "Complete")]
    [InlineData(400 * 3600, DisputeCaseStatus.Rejected, "Complete")]
    public async Task ListAndDetailReportTheComputedSlaStatus(int secondsOld, DisputeCaseStatus status, string expected)
    {
        var reference = $"DSP-SLA-{Guid.NewGuid():N}"[..20];
        var created = UxApp.Start.AddSeconds(-secondsOld);
        var id = await _app.SeedDisputeAsync(status: status, createdAt: created, reference: reference);
        using var agent = await _app.SignInAsync("agent.demo");

        using var list = await UxApp.ReadJsonAsync(await agent.GetAsync($"/api/v1/agent/disputes?search={reference}"));
        var item = Assert.Single(list.RootElement.GetProperty("items").EnumerateArray());
        using var detail = await UxApp.ReadJsonAsync(await agent.GetAsync($"/api/v1/agent/disputes/{id}"));

        foreach (var element in new[] { item, detail.RootElement })
        {
            Assert.Equal(expected, element.GetProperty("slaStatus").GetString());
            Assert.Equal(created.AddHours(120), element.GetProperty("slaDueAt").GetDateTimeOffset());
        }
    }

    [Fact]
    public async Task SlaWindowsAreConfigurable()
    {
        var app = new UxApp(_factory, settings: new Dictionary<string, string?>
        {
            ["Sla:ReviewHours"] = "10", ["Sla:DueSoonHours"] = "2"
        });
        var id = await app.SeedDisputeAsync(createdAt: UxApp.Start.AddHours(-9));
        using var agent = await app.SignInAsync("agent.demo");
        using var detail = await UxApp.ReadJsonAsync(await agent.GetAsync($"/api/v1/agent/disputes/{id}"));

        Assert.Equal("DueSoon", detail.RootElement.GetProperty("slaStatus").GetString());
        Assert.Equal(UxApp.Start.AddHours(-9).AddHours(10), detail.RootElement.GetProperty("slaDueAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task CustomerSeesTheExpectedResolutionDateOnlyWhileTheCaseIsOpen()
    {
        var open = await _app.SeedDisputeAsync(createdAt: UxApp.Start.AddHours(-24));
        var rejected = await _app.SeedDisputeAsync(status: DisputeCaseStatus.Rejected);
        using var customer = await _app.SignInAsync("customer.demo");

        using var openBody = await UxApp.ReadJsonAsync(await customer.GetAsync($"/api/v1/disputes/{open}"));
        using var rejectedBody = await UxApp.ReadJsonAsync(await customer.GetAsync($"/api/v1/disputes/{rejected}"));

        Assert.Equal(UxApp.Start.AddHours(96), openBody.RootElement.GetProperty("expectedResolutionBy").GetDateTimeOffset());
        Assert.False(rejectedBody.RootElement.TryGetProperty("expectedResolutionBy", out _));
        Assert.False(rejectedBody.RootElement.GetProperty("canWithdraw").GetBoolean());
    }
}

public sealed class AgentAssignmentEndpointsTests : IClassFixture<SqliteWebApplicationFactory>
{
    private readonly UxApp _app;

    public AgentAssignmentEndpointsTests(SqliteWebApplicationFactory factory) => _app = new UxApp(factory);

    [Fact]
    public async Task ClaimAssignsTheCaseWithoutTouchingVersionTimelineOrOutbox()
    {
        var id = await _app.SeedDisputeAsync();
        var agentId = await _app.UserIdAsync("agent.demo");
        using var agent = await _app.SignInAsync("agent.demo");
        agent.DefaultRequestHeaders.Add("X-Correlation-ID", "claim-correlation");

        using var response = await agent.PostAsync($"/api/v1/agent/disputes/{id}/assignment", null);
        using var body = await UxApp.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(1, body.RootElement.GetProperty("version").GetInt64());
        var assignee = body.RootElement.GetProperty("assignedTo");
        Assert.Equal(agentId, assignee.GetProperty("employeeId").GetGuid());
        Assert.Equal("agent.demo", assignee.GetProperty("displayName").GetString());
        Assert.Single(body.RootElement.GetProperty("timeline").EnumerateArray());
        await _app.WithDatabaseAsync(async database =>
        {
            Assert.Equal(1, (await database.Disputes.AsNoTracking().SingleAsync(item => item.Id == id)).Version);
            Assert.Empty(await database.OutboxMessages.Where(item => item.AggregateId == id).ToArrayAsync());
            Assert.Empty(await database.DisputeCommandAuditEvents.Where(item => item.DisputeId == id).ToArrayAsync());
            var audit = await database.DisputeAssignmentAuditEvents.AsNoTracking().SingleAsync(item => item.DisputeId == id);
            Assert.Equal(DisputeAssignmentAction.Claimed, audit.Action);
            Assert.Equal(agentId, audit.EmployeeId);
            Assert.Equal("claim-correlation", audit.CorrelationId);
            Assert.Equal(UxApp.Start, audit.OccurredAt);
        });
    }

    [Fact]
    public async Task ClaimingYourOwnCaseAgainIsIdempotentAndNotAuditedTwice()
    {
        var id = await _app.SeedDisputeAsync();
        using var agent = await _app.SignInAsync("agent.demo");
        using var first = await agent.PostAsync($"/api/v1/agent/disputes/{id}/assignment", null);
        using var second = await agent.PostAsync($"/api/v1/agent/disputes/{id}/assignment", null);
        using var body = await UxApp.ReadJsonAsync(second);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("agent.demo", body.RootElement.GetProperty("assignedTo").GetProperty("displayName").GetString());
        Assert.Equal(1, await _app.WithDatabaseAsync(database =>
            database.DisputeAssignmentAuditEvents.CountAsync(item => item.DisputeId == id)));
    }

    [Fact]
    public async Task ClaimingACaseAssignedToSomeoneElseConflictsAndKeepsTheAssignee()
    {
        var id = await _app.SeedDisputeAsync();
        using var supervisor = await _app.SignInAsync("supervisor.demo");
        using var agent = await _app.SignInAsync("agent.demo");
        using var claimed = await supervisor.PostAsync($"/api/v1/agent/disputes/{id}/assignment", null);
        using var response = await agent.PostAsync($"/api/v1/agent/disputes/{id}/assignment", null);
        using var body = await UxApp.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("dispute.alreadyAssigned", UxApp.Code(body));
        var supervisorId = await _app.UserIdAsync("supervisor.demo");
        Assert.Equal(supervisorId, await _app.WithDatabaseAsync(database =>
            database.DisputeAssignments.Where(item => item.DisputeId == id).Select(item => item.EmployeeId).SingleAsync()));
        Assert.Equal(1, await _app.WithDatabaseAsync(database =>
            database.DisputeAssignmentAuditEvents.CountAsync(item => item.DisputeId == id)));
    }

    [Fact]
    public async Task UnknownAndMalformedIdsAreRejected()
    {
        using var agent = await _app.SignInAsync("agent.demo");
        using var unknown = await agent.PostAsync($"/api/v1/agent/disputes/{Guid.NewGuid()}/assignment", null);
        using var unknownRelease = await agent.DeleteAsync($"/api/v1/agent/disputes/{Guid.NewGuid()}/assignment");
        using var malformed = await agent.PostAsync("/api/v1/agent/disputes/not-a-guid/assignment", null);
        using var unknownBody = await UxApp.ReadJsonAsync(unknown);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("dispute.notFound", UxApp.Code(unknownBody));
        Assert.Equal(HttpStatusCode.NotFound, unknownRelease.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
    }

    [Fact]
    public async Task OnlyTheAssigneeOrASupervisorMayRelease()
    {
        var id = await _app.SeedDisputeAsync();
        var supervisorId = await _app.UserIdAsync("supervisor.demo");
        var agentId = await _app.UserIdAsync("agent.demo");
        using var supervisor = await _app.SignInAsync("supervisor.demo");
        using var agent = await _app.SignInAsync("agent.demo");
        using var claimed = await supervisor.PostAsync($"/api/v1/agent/disputes/{id}/assignment", null);

        using var denied = await agent.DeleteAsync($"/api/v1/agent/disputes/{id}/assignment");
        using var deniedBody = await UxApp.ReadJsonAsync(denied);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("dispute.assignmentForbidden", UxApp.Code(deniedBody));

        using var released = await supervisor.DeleteAsync($"/api/v1/agent/disputes/{id}/assignment");
        using var releasedBody = await UxApp.ReadJsonAsync(released);
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        Assert.False(releasedBody.RootElement.TryGetProperty("assignedTo", out _));

        // A supervisor can also take a case away from an agent.
        using var agentClaim = await agent.PostAsync($"/api/v1/agent/disputes/{id}/assignment", null);
        using var forced = await supervisor.DeleteAsync($"/api/v1/agent/disputes/{id}/assignment");
        Assert.Equal(HttpStatusCode.OK, forced.StatusCode);

        var audit = await _app.WithDatabaseAsync(database => database.DisputeAssignmentAuditEvents.AsNoTracking()
            .Where(item => item.DisputeId == id).OrderBy(item => item.OccurredAt).ThenBy(item => item.Action).ToArrayAsync());
        Assert.Equal(4, audit.Length);
        Assert.Equal(2, audit.Count(item => item.Action == DisputeAssignmentAction.Claimed));
        var forcedRelease = audit.Last(item => item.Action == DisputeAssignmentAction.Released);
        Assert.Equal(supervisorId, forcedRelease.EmployeeId);
        Assert.Equal(agentId, forcedRelease.PreviousEmployeeId);
    }

    [Fact]
    public async Task AssigneeCanReleaseAndReleasingAnUnassignedCaseIsANoOp()
    {
        var id = await _app.SeedDisputeAsync();
        using var agent = await _app.SignInAsync("agent.demo");
        using var noOp = await agent.DeleteAsync($"/api/v1/agent/disputes/{id}/assignment");
        Assert.Equal(HttpStatusCode.OK, noOp.StatusCode);
        Assert.Equal(0, await _app.WithDatabaseAsync(database =>
            database.DisputeAssignmentAuditEvents.CountAsync(item => item.DisputeId == id)));

        using var claimed = await agent.PostAsync($"/api/v1/agent/disputes/{id}/assignment", null);
        using var released = await agent.DeleteAsync($"/api/v1/agent/disputes/{id}/assignment");
        using var body = await UxApp.ReadJsonAsync(released);

        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        Assert.False(body.RootElement.TryGetProperty("assignedTo", out _));
        Assert.Equal(1, body.RootElement.GetProperty("version").GetInt64());
        Assert.Empty(await _app.WithDatabaseAsync(database =>
            database.DisputeAssignments.Where(item => item.DisputeId == id).ToArrayAsync()));
    }

    [Fact]
    public async Task AssignmentNeverBlocksCaseProcessingOrChangesItsVersion()
    {
        var id = await _app.SeedDisputeAsync();
        using var supervisor = await _app.SignInAsync("supervisor.demo");
        using var agent = await _app.SignInAsync("agent.demo");
        using var claimed = await supervisor.PostAsync($"/api/v1/agent/disputes/{id}/assignment", null);

        using var command = await agent.PostAsJsonAsync($"/api/v1/agent/disputes/{id}/commands",
            new { command = "StartReview", expectedVersion = 1 });
        using var body = await UxApp.ReadJsonAsync(command);

        Assert.Equal(HttpStatusCode.OK, command.StatusCode);
        Assert.Equal(2, body.RootElement.GetProperty("version").GetInt64());
        using var detail = await UxApp.ReadJsonAsync(await agent.GetAsync($"/api/v1/agent/disputes/{id}"));
        Assert.Equal("supervisor.demo", detail.RootElement.GetProperty("assignedTo").GetProperty("displayName").GetString());
        Assert.Equal(2, detail.RootElement.GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task ListFiltersByMineUnassignedAndAll()
    {
        var tag = $"DSP-AS{Guid.NewGuid():N}"[..14];
        var mine = await _app.SeedDisputeAsync(reference: tag + "-1");
        var theirs = await _app.SeedDisputeAsync(reference: tag + "-2");
        var free = await _app.SeedDisputeAsync(reference: tag + "-3");
        using var agent = await _app.SignInAsync("agent.demo");
        using var supervisor = await _app.SignInAsync("supervisor.demo");
        using var a = await agent.PostAsync($"/api/v1/agent/disputes/{mine}/assignment", null);
        using var s = await supervisor.PostAsync($"/api/v1/agent/disputes/{theirs}/assignment", null);

        async Task<JsonElement[]> ListAsync(string query)
        {
            using var response = await agent.GetAsync($"/api/v1/agent/disputes?search={tag}{query}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = await UxApp.ReadJsonAsync(response);
            return body.RootElement.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToArray();
        }

        Assert.Equal(new[] { mine }, (await ListAsync("&assignment=mine")).Select(item => item.GetProperty("id").GetGuid()));
        Assert.Equal(new[] { free }, (await ListAsync("&assignment=unassigned")).Select(item => item.GetProperty("id").GetGuid()));
        var all = await ListAsync("&assignment=all");
        Assert.Equal(3, all.Length);
        Assert.Equal(3, (await ListAsync("")).Length);
        Assert.Equal("agent.demo", all.Single(item => item.GetProperty("id").GetGuid() == mine)
            .GetProperty("assignedTo").GetProperty("displayName").GetString());
        Assert.False(all.Single(item => item.GetProperty("id").GetGuid() == free).TryGetProperty("assignedTo", out _));
    }

    [Theory]
    [InlineData("everyone")]
    [InlineData("Mine")]
    [InlineData("")]
    public async Task InvalidAssignmentFilterIsRejected(string value)
    {
        using var agent = await _app.SignInAsync("agent.demo");
        using var response = await agent.GetAsync($"/api/v1/agent/disputes?assignment={value}");
        using var body = await UxApp.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = body.RootElement.GetProperty("errors")[0];
        Assert.Equal("/assignment", error.GetProperty("pointer").GetString());
        Assert.Equal("assignment.invalid", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AssignmentAuditIsAppendOnly()
    {
        var id = await _app.SeedDisputeAsync();
        using var agent = await _app.SignInAsync("agent.demo");
        using var claimed = await agent.PostAsync($"/api/v1/agent/disputes/{id}/assignment", null);

        await _app.WithDatabaseAsync(async database =>
        {
            await Assert.ThrowsAsync<SqliteException>(() => database.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"DisputeAssignmentAuditEvents\" SET \"CorrelationId\" = 'changed' WHERE \"DisputeId\" = {id}"));
            await Assert.ThrowsAsync<SqliteException>(() => database.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM \"DisputeAssignmentAuditEvents\" WHERE \"DisputeId\" = {id}"));
        });
    }

    [Fact]
    public async Task CustomersCannotAssignAndStaffMutationsRequireCsrf()
    {
        var id = await _app.SeedDisputeAsync();
        using var customer = await _app.SignInAsync("customer.demo");
        using var forbidden = await customer.PostAsync($"/api/v1/agent/disputes/{id}/assignment", null);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var agent = await _app.SignInAsync("agent.demo");
        agent.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var noCsrf = await agent.PostAsync($"/api/v1/agent/disputes/{id}/assignment", null);
        using var noCsrfRelease = await agent.DeleteAsync($"/api/v1/agent/disputes/{id}/assignment");
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrfRelease.StatusCode);
    }
}

public sealed class AgentFinancialOperationEndpointsTests : IClassFixture<SqliteWebApplicationFactory>
{
    private readonly UxApp _app;

    public AgentFinancialOperationEndpointsTests(SqliteWebApplicationFactory factory) => _app = new UxApp(factory);

    [Fact]
    public async Task CaseWithoutIntentsReportsAnEmptyList()
    {
        var id = await _app.SeedDisputeAsync();
        using var agent = await _app.SignInAsync("agent.demo");
        using var detail = await UxApp.ReadJsonAsync(await agent.GetAsync($"/api/v1/agent/disputes/{id}"));

        var operations = detail.RootElement.GetProperty("financialOperations");
        Assert.Equal(JsonValueKind.Array, operations.ValueKind);
        Assert.Equal(0, operations.GetArrayLength());
    }

    [Fact]
    public async Task IntentStatesMapToTheFiveStaffStatusesOldestFirstWithoutPayloads()
    {
        var id = await _app.SeedDisputeAsync(type: TransactionType.CardPurchase);
        var now = UxApp.Start;
        (FinancialOperationKind Kind, FinancialOperationStage Stage, string? Outcome, DateTimeOffset? Lease)[] rows =
        [
            (FinancialOperationKind.CardNetworkInitiate, FinancialOperationStage.Completed, "Succeeded", null),
            (FinancialOperationKind.CardNetworkForwardToAcquirer, FinancialOperationStage.Pending, null, null),
            (FinancialOperationKind.DebitReversal, FinancialOperationStage.Dispatched, null, now.AddMinutes(1)),
            (FinancialOperationKind.BankChargeAdjustment, FinancialOperationStage.Dispatched, null, now.AddMinutes(-1)),
            (FinancialOperationKind.CardNetworkSettlement, FinancialOperationStage.ResultRecorded, "Failed", now.AddMinutes(1)),
            (FinancialOperationKind.CardNetworkInitiate, FinancialOperationStage.Completed, "OutcomeUnknown", null)
        ];
        await _app.WithDatabaseAsync(async database =>
        {
            for (var index = 0; index < rows.Length; index++)
                database.FinancialOperationIntents.Add(new PersistedFinancialOperation
                {
                    Id = Guid.NewGuid(), DisputeId = id, CustomerId = Guid.Parse(UxApp.DemoCustomerId),
                    Kind = rows[index].Kind, OperationReference = $"OP-{id:N}-{index}", ExpectedVersion = 1,
                    CorrelationId = "intent-secret-correlation", Stage = rows[index].Stage,
                    Outcome = rows[index].Outcome, LeaseUntilAt = rows[index].Lease,
                    CreatedAt = now.AddMinutes(index), UpdatedAt = now.AddMinutes(index).AddSeconds(30)
                });
            await database.SaveChangesAsync();
        });
        using var agent = await _app.SignInAsync("agent.demo");
        using var response = await agent.GetAsync($"/api/v1/agent/disputes/{id}");
        using var detail = await UxApp.ReadJsonAsync(response);

        var operations = detail.RootElement.GetProperty("financialOperations").EnumerateArray().ToArray();
        Assert.Equal(new[] { "Succeeded", "Pending", "InFlight", "Unknown", "Failed", "Unknown" },
            operations.Select(item => item.GetProperty("status").GetString()));
        Assert.Equal(rows.Select(row => row.Kind.ToString()), operations.Select(item => item.GetProperty("kind").GetString()));
        Assert.Equal($"OP-{id:N}-0", operations[0].GetProperty("operationReference").GetString());
        Assert.Equal(now.AddSeconds(30), operations[0].GetProperty("updatedAt").GetDateTimeOffset());
        Assert.All(operations, item => Assert.Equal(
            new[] { "kind", "operationReference", "status", "updatedAt" },
            item.EnumerateObject().Select(property => property.Name).Order()));
        Assert.DoesNotContain("intent-secret-correlation", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ExecutedBankChargeAdjustmentIsReportedAsSucceededWithItsCompletionTime()
    {
        var id = await _app.SeedDisputeAsync();
        using var agent = await _app.SignInAsync("agent.demo");
        long version = 1;
        foreach (var command in new[] { "StartReview", "Approve", "RequestAdjustment", "ExecuteAdjustment" })
        {
            using var step = await agent.PostAsJsonAsync($"/api/v1/agent/disputes/{id}/commands",
                new { command, expectedVersion = version });
            Assert.Equal(HttpStatusCode.OK, step.StatusCode);
            version++;
        }

        using var detail = await UxApp.ReadJsonAsync(await agent.GetAsync($"/api/v1/agent/disputes/{id}"));
        var operation = Assert.Single(detail.RootElement.GetProperty("financialOperations").EnumerateArray());
        Assert.Equal("BankChargeAdjustment", operation.GetProperty("kind").GetString());
        Assert.Equal("Succeeded", operation.GetProperty("status").GetString());
        Assert.Equal(UxApp.Start, operation.GetProperty("updatedAt").GetDateTimeOffset());
        Assert.StartsWith("MOCK-FEE-", operation.GetProperty("operationReference").GetString());
    }
}
