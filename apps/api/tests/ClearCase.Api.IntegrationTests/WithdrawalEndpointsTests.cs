using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Chargebacks;
using ClearCase.Application.Transactions;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;
using ClearCase.Infrastructure.Chargebacks;
using ClearCase.Infrastructure.Disputes;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Api.IntegrationTests;

public sealed class WithdrawalEndpointsTests : IClassFixture<SqliteWebApplicationFactory>
{
    private const string Description = "You withdrew this dispute. No payment adjustment was made.";
    private readonly UxApp _app;

    public WithdrawalEndpointsTests(SqliteWebApplicationFactory factory) => _app = new UxApp(factory);

    [Fact]
    public async Task WithdrawalClosesTheCaseAtomicallyWithTimelineOutboxAndVersion()
    {
        var id = await _app.SeedDisputeAsync(status: DisputeCaseStatus.UnderReview);
        using var customer = await _app.SignInAsync("customer.demo");
        using var before = await customer.GetAsync($"/api/v1/disputes/{id}");
        using var beforeBody = await UxApp.ReadJsonAsync(before);
        Assert.True(beforeBody.RootElement.GetProperty("canWithdraw").GetBoolean());
        Assert.Equal("2026-11-17T10:00:00+00:00", beforeBody.RootElement.GetProperty("expectedResolutionBy").GetString());

        customer.DefaultRequestHeaders.Add("X-Correlation-ID", "withdraw-correlation");
        using var response = await customer.PostAsJsonAsync($"/api/v1/disputes/{id}/withdrawals",
            new { expectedVersion = 1, reason = "  Sorted it out with the merchant  " });
        using var body = await UxApp.ReadJsonAsync(response);
        var root = body.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("Closed", root.GetProperty("caseStatus").GetString());
        Assert.Equal("Pending", root.GetProperty("decisionStatus").GetString());
        Assert.Equal("NotInitiated", root.GetProperty("financialStatus").GetString());
        Assert.Equal(2, root.GetProperty("version").GetInt64());
        Assert.False(root.GetProperty("canWithdraw").GetBoolean());
        Assert.False(root.TryGetProperty("expectedResolutionBy", out _));
        var last = root.GetProperty("timeline").EnumerateArray().Last();
        Assert.Equal("CustomerWithdrawn", last.GetProperty("eventType").GetString());
        Assert.Equal(2, last.GetProperty("sequenceNumber").GetInt32());
        Assert.Equal(Description, last.GetProperty("description").GetString());

        await _app.WithDatabaseAsync(async database =>
        {
            var row = await database.Disputes.AsNoTracking().SingleAsync(item => item.Id == id);
            Assert.Equal(2, row.Version);
            Assert.Equal(DisputeCaseStatus.Closed, row.CaseStatus);
            var outbox = await database.OutboxMessages.AsNoTracking().SingleAsync(item => item.AggregateId == id);
            Assert.Equal("dispute.withdrawn.v1", outbox.EventType);
            Assert.Equal(2, outbox.AggregateVersion);
            Assert.Equal("withdraw-correlation", outbox.CorrelationId);
            Assert.Contains("\"Withdrawn\"", outbox.Payload);
            Assert.DoesNotContain("merchant", outbox.Payload);
            var withdrawal = await database.DisputeWithdrawals.AsNoTracking().SingleAsync(item => item.DisputeId == id);
            Assert.Equal("Sorted it out with the merchant", withdrawal.Reason);
        });
    }

    [Fact]
    public async Task WithdrawalEnvelopeIsAcceptedByTheKafkaConsumerContract()
    {
        var id = await _app.SeedDisputeAsync();
        using var customer = await _app.SignInAsync("customer.demo");
        using var response = await customer.PostAsJsonAsync($"/api/v1/disputes/{id}/withdrawals", new { expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var disposition = await _app.WithDatabaseAsync(async database =>
        {
            var message = await database.OutboxMessages.AsNoTracking().SingleAsync(item => item.AggregateId == id);
            var record = KafkaOutboxPublisher.CreateMessage(new OutboxEvent(message.EventId, message.AggregateId,
                message.AggregateVersion, message.EventType, message.EventVersion, message.CorrelationId,
                message.Payload, message.OccurredAt, 0)).Value;
            var processor = new SqliteKafkaRecordProcessor(database,
                new SqliteInboxProcessor(database, TimeProvider.System), TimeProvider.System);
            return await processor.ProcessAsync("ux-consumer", "disputes.case-events.v1", 0, 1, record, default);
        });
        Assert.Equal(KafkaRecordDisposition.Applied, disposition);
    }

    [Fact]
    public async Task ACustomerCanOpenANewDisputeForTheSameTransactionAfterWithdrawing()
    {
        using var customer = await _app.SignInAsync("customer.demo");
        var payload = new
        {
            transactionId = "00000000-0000-0000-0000-000000000007",
            reasonCode = "duplicateCharge",
            description = "I was charged twice for the same purchase."
        };
        using var first = await customer.SubmitDisputeAsync("/api/v1/disputes", payload);
        using var firstBody = await UxApp.ReadJsonAsync(first);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.True(firstBody.RootElement.GetProperty("canWithdraw").GetBoolean());
        var firstId = firstBody.RootElement.GetProperty("id").GetGuid();

        using var duplicate = await customer.SubmitDisputeAsync("/api/v1/disputes", payload);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var withdrawn = await customer.PostAsJsonAsync($"/api/v1/disputes/{firstId}/withdrawals",
            new { expectedVersion = firstBody.RootElement.GetProperty("version").GetInt64() });
        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);

        using var second = await customer.SubmitDisputeAsync("/api/v1/disputes", payload);
        using var secondBody = await UxApp.ReadJsonAsync(second);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.NotEqual(firstId, secondBody.RootElement.GetProperty("id").GetGuid());
    }

    public static TheoryData<string> BlockedStates() =>
        ["closed", "rejected", "approved", "decided-rejected", "payment-requested", "payment-unknown", "chargeback", "escalated"];

    [Theory]
    [MemberData(nameof(BlockedStates))]
    public async Task WithdrawalIsRefusedOnceTheCaseIsFinishedDecidedPaidChargedBackOrEscalated(string state)
    {
        var (status, decision, financial) = state switch
        {
            "closed" => (DisputeCaseStatus.Closed, DisputeDecisionStatus.Pending, DisputeFinancialStatus.NotInitiated),
            "rejected" => (DisputeCaseStatus.Rejected, DisputeDecisionStatus.Pending, DisputeFinancialStatus.NotInitiated),
            "approved" => (DisputeCaseStatus.UnderReview, DisputeDecisionStatus.Approved, DisputeFinancialStatus.NotInitiated),
            "decided-rejected" => (DisputeCaseStatus.UnderReview, DisputeDecisionStatus.Rejected, DisputeFinancialStatus.NotInitiated),
            "payment-requested" => (DisputeCaseStatus.ReversalRequested, DisputeDecisionStatus.Approved, DisputeFinancialStatus.Requested),
            "payment-unknown" => (DisputeCaseStatus.ReversalRequested, DisputeDecisionStatus.Approved, DisputeFinancialStatus.OutcomeUnknown),
            _ => (DisputeCaseStatus.UnderReview, DisputeDecisionStatus.Pending, DisputeFinancialStatus.NotInitiated)
        };
        var id = await _app.SeedDisputeAsync(
            type: state == "chargeback" ? TransactionType.CardPurchase : TransactionType.BankCharge,
            status: status, decision: decision, financial: financial);
        long version = 1;
        if (state == "chargeback")
            await _app.WithDatabaseAsync(async database =>
            {
                database.Chargebacks.Add(new PersistedChargeback
                {
                    DisputeId = id, Reference = $"CB-{Guid.NewGuid():N}", ReasonCode = "unrecognisedTransaction",
                    Status = ChargebackStatus.Initiated, LastOperationOutcome = CardNetworkOutcome.Succeeded
                });
                await database.SaveChangesAsync();
            });
        using var customer = await _app.SignInAsync("customer.demo");
        if (state == "escalated")
        {
            using var agent = await _app.SignInAsync("agent.demo");
            using var escalation = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/agent/disputes/{id}/escalations")
            {
                Content = JsonContent.Create(new
                {
                    reason = "Additional investigation required",
                    note = "Supervisor review is required before processing this case."
                })
            };
            escalation.Headers.Add("Idempotency-Key", "withdraw-escalated-1");
            using var created = await agent.SendAsync(escalation);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            version = await _app.WithDatabaseAsync(database =>
                database.Disputes.AsNoTracking().Where(item => item.Id == id).Select(item => item.Version).SingleAsync());
        }

        using var view = await customer.GetAsync($"/api/v1/disputes/{id}");
        using var viewBody = await UxApp.ReadJsonAsync(view);
        Assert.False(viewBody.RootElement.GetProperty("canWithdraw").GetBoolean());

        using var response = await customer.PostAsJsonAsync($"/api/v1/disputes/{id}/withdrawals", new { expectedVersion = version });
        using var body = await UxApp.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("dispute.withdrawalNotAllowed", UxApp.Code(body));
        await _app.WithDatabaseAsync(async database =>
        {
            Assert.Equal(version, (await database.Disputes.AsNoTracking().SingleAsync(item => item.Id == id)).Version);
            Assert.Empty(await database.DisputeWithdrawals.Where(item => item.DisputeId == id).ToArrayAsync());
            Assert.DoesNotContain(await database.DisputeEvents.Where(item => item.DisputeId == id).ToArrayAsync(),
                item => item.EventType == "CustomerWithdrawn");
        });
    }

    [Fact]
    public async Task StaleVersionIsAConcurrencyConflictAndChangesNothing()
    {
        var id = await _app.SeedDisputeAsync();
        using var customer = await _app.SignInAsync("customer.demo");
        using var response = await customer.PostAsJsonAsync($"/api/v1/disputes/{id}/withdrawals", new { expectedVersion = 7 });
        using var body = await UxApp.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("dispute.concurrencyConflict", UxApp.Code(body));
        Assert.Equal(DisputeCaseStatus.Submitted, await _app.WithDatabaseAsync(database =>
            database.Disputes.AsNoTracking().Where(item => item.Id == id).Select(item => item.CaseStatus).SingleAsync()));
    }

    [Fact]
    public async Task WithdrawingTwiceWithTheSameVersionConflicts()
    {
        var id = await _app.SeedDisputeAsync();
        using var customer = await _app.SignInAsync("customer.demo");
        using var first = await customer.PostAsJsonAsync($"/api/v1/disputes/{id}/withdrawals", new { expectedVersion = 1 });
        using var second = await customer.PostAsJsonAsync($"/api/v1/disputes/{id}/withdrawals", new { expectedVersion = 1 });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"expectedVersion\":0}")]
    [InlineData("{\"expectedVersion\":-3}")]
    public async Task MissingOrNonPositiveVersionIsRejected(string json)
    {
        var id = await _app.SeedDisputeAsync();
        using var customer = await _app.SignInAsync("customer.demo");
        using var response = await customer.PostAsync($"/api/v1/disputes/{id}/withdrawals",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        using var body = await UxApp.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("dispute.invalidRequest", UxApp.Code(body));
        Assert.Equal("/expectedVersion", body.RootElement.GetProperty("errors")[0].GetProperty("pointer").GetString());
    }

    [Fact]
    public async Task ReasonLongerThanFiveHundredCharactersIsRejectedButExactlyFiveHundredIsAccepted()
    {
        var id = await _app.SeedDisputeAsync();
        using var customer = await _app.SignInAsync("customer.demo");
        using var tooLong = await customer.PostAsJsonAsync($"/api/v1/disputes/{id}/withdrawals",
            new { expectedVersion = 1, reason = new string('r', 501) });
        using var tooLongBody = await UxApp.ReadJsonAsync(tooLong);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal("dispute.invalidRequest", UxApp.Code(tooLongBody));

        using var allowed = await customer.PostAsJsonAsync($"/api/v1/disputes/{id}/withdrawals",
            new { expectedVersion = 1, reason = new string('r', 500) });
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task OtherCustomersAndUnknownCasesAreNotFoundAndUntouched()
    {
        var id = await _app.SeedDisputeAsync(UxApp.OtherCustomerId);
        using var demo = await _app.SignInAsync("customer.demo");
        using var other = await demo.PostAsJsonAsync($"/api/v1/disputes/{id}/withdrawals", new { expectedVersion = 1 });
        using var unknown = await demo.PostAsJsonAsync($"/api/v1/disputes/{Guid.NewGuid()}/withdrawals", new { expectedVersion = 1 });
        using var otherBody = await UxApp.ReadJsonAsync(other);

        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("dispute.notFound", UxApp.Code(otherBody));
        Assert.Equal(1, await _app.WithDatabaseAsync(database =>
            database.Disputes.AsNoTracking().Where(item => item.Id == id).Select(item => item.Version).SingleAsync()));
    }

    [Fact]
    public async Task StaffCannotWithdrawAndMutatingRequestsRequireCsrf()
    {
        var id = await _app.SeedDisputeAsync();
        using var agent = await _app.SignInAsync("agent.demo");
        using var forbidden = await agent.PostAsJsonAsync($"/api/v1/disputes/{id}/withdrawals", new { expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var customer = await _app.SignInAsync("customer.demo");
        customer.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var noCsrf = await customer.PostAsJsonAsync($"/api/v1/disputes/{id}/withdrawals", new { expectedVersion = 1 });
        using var body = await UxApp.ReadJsonAsync(noCsrf);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        Assert.Equal("auth.csrfInvalid", UxApp.Code(body));
    }
}
