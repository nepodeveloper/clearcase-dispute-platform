using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClearCase.Application.Disputes;
using ClearCase.Domain.DisputeEligibility;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;
using ClearCase.Infrastructure.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClearCase.Api.IntegrationTests;

public sealed class OutboxRecoveryEndpointsTests : IClassFixture<SqliteWebApplicationFactory>
{
    private readonly SqliteWebApplicationFactory _factory;
    private const string Route = "/api/v1/supervisor/outbox/parked";
    private static readonly Guid CustomerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TransactionId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public OutboxRecoveryEndpointsTests(SqliteWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task OnlySupervisorCanInspectOrRequeueParkedEventsAndAllRecoveryIsAudited()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
            var occurredAt = new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
            var source = new DisputeTransaction(TransactionId, TransactionCategory.CardPurchase,
                25m, "ZAR", occurredAt.AddDays(-1), TransactionStatus.Posted,
                true, true, null, null, null, null);
            var eligibility = new DisputeEligibilityResult(
                DisputeEligibilityOutcome.EligibleToSubmit, [],
                "Eligible for synthetic review.", "test-v1", occurredAt);
            Assert.Equal(DisputeAddResult.Added, await new SqliteDisputeStore(db).AddAsync(
                Dispute.Submit(Guid.NewGuid(), "DSP-RECOVERY" + Guid.NewGuid().ToString("N")[..12],
                    CustomerId, source, "unrecognisedTransaction",
                    "Please investigate the synthetic card payment.",
                    eligibility, Guid.NewGuid()), default));
            var store = new SqliteOutboxStore(db);
            var claim = (await store.ClaimNextAsync(occurredAt.AddMinutes(1),
                TimeSpan.FromMinutes(2), default))!;
            Assert.NotNull(claim);
            Assert.True(await store.ParkAsync(claim.Message.EventId, claim.LeaseId,
                occurredAt.AddMinutes(2), "delivery.attemptsExhausted", default));
        }

        using var anonymous = CreateClient();
        using var anonymousResponse = await anonymous.GetAsync(Route);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        using var customer = await SignInAsync("customer.demo");
        using var customerResponse = await customer.GetAsync(Route);
        Assert.Equal(HttpStatusCode.Forbidden, customerResponse.StatusCode);
        using var agent = await SignInAsync("agent.demo");
        using var agentResponse = await agent.GetAsync(Route);
        Assert.Equal(HttpStatusCode.Forbidden, agentResponse.StatusCode);
        using var supervisor = await SignInAsync("supervisor.demo");
        using var listResponse = await supervisor.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var listed = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var row = Assert.Single(listed.RootElement.EnumerateArray());
        var eventId = row.GetProperty("eventId").GetGuid();
        Assert.Equal("delivery.attemptsExhausted", row.GetProperty("reasonCode").GetString());
        Assert.False(row.TryGetProperty("payload", out _));
        Assert.False(row.TryGetProperty("correlationId", out _));
        Assert.True(listResponse.Headers.CacheControl?.NoStore);

        var path = $"/api/v1/supervisor/outbox/{eventId}/requeue";
        using var forbidden = await agent.PostAsJsonAsync(path, new
        {
            reason = "Check the broker before requeue; Agent cannot approve this."
        });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var invalid = await supervisor.PostAsJsonAsync(path, new { reason = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var requeue = await supervisor.PostAsJsonAsync(path, new
        {
            reason = "Broker inspected; original event may be safely replayed."
        });
        Assert.Equal(HttpStatusCode.OK, requeue.StatusCode);
        using var repeated = await supervisor.PostAsJsonAsync(path, new
        {
            reason = "This is an intentional duplicate recovery attempt."
        });
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var database = verificationScope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        var item = await database.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(eventId, item.EventId);
        Assert.Null(item.ParkedAt);
        Assert.Null(item.PublishedAt);
        var audit = await database.Set<PersistedOutboxRecoveryAudit>().AsNoTracking().SingleAsync();
        var supervisorId = await database.Users.Where(user => user.UserName == "supervisor.demo")
            .Select(user => user.Id).SingleAsync();
        Assert.Equal(supervisorId, audit.EmployeeId);
    }

    private HttpClient CreateClient()
    {
        var server = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Authentication:EnableDevelopmentCompatibilityPath"] = "false"
                }));
        });
        return server.CreateClient();
    }

    private async Task<HttpClient> SignInAsync(string username)
    {
        var client = CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { username, password = TestIdentityPasswords.For(username) });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",
            json.RootElement.GetProperty("csrfToken").GetString());
        return client;
    }
}
