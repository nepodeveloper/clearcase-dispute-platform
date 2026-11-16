using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClearCase.Application.Transactions;
using ClearCase.Domain.DisputeEligibility;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;
using ClearCase.Infrastructure.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
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
        Assert.Equal(false,
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

    private async Task<HttpClient> CreateSignedInClientAsync(string username)
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FrozenTimeProvider(CreatedAt));
            });
        }).CreateClient();
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
}
