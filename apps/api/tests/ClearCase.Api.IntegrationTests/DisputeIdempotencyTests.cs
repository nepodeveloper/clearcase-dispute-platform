using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClearCase.Infrastructure.Transactions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClearCase.Api.IntegrationTests;

public sealed class DisputeIdempotencyTests : IClassFixture<SqliteWebApplicationFactory>
{
    private const string ReplayCardId = "00000000-0000-0000-0000-000000000001";
    private const string ChangedPayloadCardId = "00000000-0000-0000-0000-000000000007";
    private const string ConcurrentSameKeyCardId = "00000000-0000-0000-0000-000000000010";
    private const string ConcurrentDifferentKeyCardId = "00000000-0000-0000-0000-000000000013";
    private const string ClosedCaseCardId = "00000000-0000-0000-0000-000000000016";
    private const string NoKeyCardId = "00000000-0000-0000-0000-000000000019";
    private const string SharedKeyCardId = "00000000-0000-0000-0000-000000000022";
    private const string InvalidKeyCardId = "00000000-0000-0000-0000-000000000025";
    private const string OtherCustomersTransactionId = "00000000-0000-0000-0000-000000000099";
    private const string OtherCustomer = "22222222-2222-2222-2222-222222222222";
    private const string Description = "I was charged twice for the same purchase.";
    private static readonly DateTimeOffset FixedInstant = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteWebApplicationFactory _factory;

    public DisputeIdempotencyTests(SqliteWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Post_SameKeyAndPayload_ReplaysOriginalCaseWithoutCreatingAnother()
    {
        using var client = CreateClient();
        var key = Guid.NewGuid().ToString();

        using var first = await Submit(client, key, ReplayCardId, Description);
        using var firstBody = await ReadJson(first);
        using var second = await Submit(client, key, ReplayCardId, "  " + Description + "  ");
        using var secondBody = await ReadJson(second);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.False(first.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal("true", second.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(first.Headers.Location, second.Headers.Location);
        Assert.Equal(firstBody.RootElement.GetProperty("id").GetGuid(), secondBody.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(firstBody.RootElement.GetProperty("reference").GetString(),
            secondBody.RootElement.GetProperty("reference").GetString());
        Assert.Equal(1, await CountDisputes(ReplayCardId));
    }

    [Fact]
    public async Task Post_SameKeyWithDifferentPayload_Returns409AndKeepsOriginalCase()
    {
        using var client = CreateClient();
        var key = Guid.NewGuid().ToString();

        using var first = await Submit(client, key, ChangedPayloadCardId, Description);
        using var second = await Submit(client, key, ChangedPayloadCardId, Description + " Changed.");
        using var body = await ReadJson(second);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        Assert.Equal("dispute.idempotencyKeyReused", body.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("correlationId").GetString()));
        Assert.DoesNotContain(Description, body.RootElement.GetRawText());
        Assert.Equal(1, await CountDisputes(ChangedPayloadCardId));
    }

    [Fact]
    public async Task Post_ConcurrentRequestsWithSameKey_CreateExactlyOneCase()
    {
        var key = Guid.NewGuid().ToString();
        var clients = Enumerable.Range(0, 6).Select(_ => CreateClient()).ToArray();
        try
        {
            var responses = await Task.WhenAll(clients.Select(client =>
                Submit(client, key, ConcurrentSameKeyCardId, Description)));
            var ids = new List<Guid>();
            foreach (var response in responses)
            {
                using (response)
                using (var body = await ReadJson(response))
                {
                    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                    ids.Add(body.RootElement.GetProperty("id").GetGuid());
                }
            }

            Assert.Single(ids.Distinct());
            Assert.Equal(1, await CountDisputes(ConcurrentSameKeyCardId));
        }
        finally
        {
            foreach (var client in clients) client.Dispose();
        }
    }

    [Fact]
    public async Task Post_ConcurrentRequestsWithDifferentKeys_CreateOneCaseAndRejectTheRest()
    {
        var clients = Enumerable.Range(0, 6).Select(_ => CreateClient()).ToArray();
        try
        {
            var responses = await Task.WhenAll(clients.Select(client =>
                Submit(client, Guid.NewGuid().ToString(), ConcurrentDifferentKeyCardId, Description)));
            var statuses = new List<HttpStatusCode>();
            foreach (var response in responses)
            {
                using (response)
                {
                    statuses.Add(response.StatusCode);
                    if (response.StatusCode == HttpStatusCode.Conflict)
                    {
                        using var body = await ReadJson(response);
                        Assert.Equal("dispute.activeCaseExists", body.RootElement.GetProperty("code").GetString());
                    }
                }
            }

            Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Created));
            Assert.Equal(5, statuses.Count(status => status == HttpStatusCode.Conflict));
            Assert.Equal(1, await CountDisputes(ConcurrentDifferentKeyCardId));
        }
        finally
        {
            foreach (var client in clients) client.Dispose();
        }
    }

    [Fact]
    public async Task Post_ReplayAfterCaseClosed_ReturnsOriginalCaseInsteadOfOpeningNewOne()
    {
        using var client = CreateClient();
        var key = Guid.NewGuid().ToString();
        using var first = await Submit(client, key, ClosedCaseCardId, Description);
        using var firstBody = await ReadJson(first);
        var id = firstBody.RootElement.GetProperty("id").GetGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Disputes\" SET \"CaseStatus\" = 'Closed' WHERE \"Id\" = {id}");
        }

        using var replay = await Submit(client, key, ClosedCaseCardId, Description);
        using var replayBody = await ReadJson(replay);

        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(id, replayBody.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("Closed", replayBody.RootElement.GetProperty("caseStatus").GetString());
        Assert.Equal(1, await CountDisputes(ClosedCaseCardId));
    }

    [Fact]
    public async Task Post_SameKeyFromDifferentCustomers_IsScopedPerCustomer()
    {
        var key = Guid.NewGuid().ToString();
        using var demoClient = CreateClient();
        using var otherClient = CreateClient(customerId: OtherCustomer);

        using var demo = await Submit(demoClient, key, SharedKeyCardId, Description);
        using var other = await Submit(otherClient, key, OtherCustomersTransactionId, Description);
        using var demoBody = await ReadJson(demo);
        using var otherBody = await ReadJson(other);

        Assert.Equal(HttpStatusCode.Created, demo.StatusCode);
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
        Assert.False(other.Headers.Contains("Idempotent-Replayed"));
        Assert.NotEqual(demoBody.RootElement.GetProperty("id").GetGuid(), otherBody.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(OtherCustomersTransactionId, otherBody.RootElement.GetProperty("transactionId").GetString());
    }

    [Theory]
    [InlineData("contains space")]
    [InlineData("key-é")]
    public async Task Post_InvalidIdempotencyKey_Returns400WithPointer(string key)
    {
        using var client = CreateClient();

        using var response = await Submit(client, key, InvalidKeyCardId, Description);
        using var body = await ReadJson(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("dispute.invalidRequest", body.RootElement.GetProperty("code").GetString());
        var error = Assert.Single(body.RootElement.GetProperty("errors").EnumerateArray());
        Assert.Equal("/headers/Idempotency-Key", error.GetProperty("pointer").GetString());
        Assert.Equal("idempotencyKey.invalid", error.GetProperty("code").GetString());
        Assert.Equal(0, await CountDisputes(InvalidKeyCardId));
    }

    [Fact]
    public async Task Post_TooLongIdempotencyKey_Returns400()
    {
        using var client = CreateClient();

        using var response = await Submit(client, new string('k', 129), InvalidKeyCardId, Description);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountDisputes(InvalidKeyCardId));
    }

    [Fact]
    public async Task Post_WithoutKey_KeepsExistingDuplicateProtection()
    {
        using var client = CreateClient();

        using var first = await Submit(client, null, NoKeyCardId, Description);
        using var second = await Submit(client, null, NoKeyCardId, Description);
        using var body = await ReadJson(second);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("dispute.activeCaseExists", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(1, await CountDisputes(NoKeyCardId));
    }

    [Fact]
    public async Task DevelopmentOpenApi_DescribesIdempotencyKeyHeader()
    {
        using var client = CreateClient("Development");
        using var body = await ReadJson(await client.GetAsync("/openapi/v1.json"));

        var parameters = body.RootElement.GetProperty("paths").GetProperty("/api/v1/disputes")
            .GetProperty("post").GetProperty("parameters").EnumerateArray();
        Assert.Contains(parameters, parameter =>
            parameter.GetProperty("name").GetString() == "Idempotency-Key"
            && parameter.GetProperty("in").GetString() == "header");
    }

    private static Task<HttpResponseMessage> Submit(
        HttpClient client, string? key, string transactionId, string description)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/disputes")
        {
            Content = JsonContent.Create(new { transactionId, reasonCode = "duplicateCharge", description })
        };
        if (key is not null)
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private async Task<int> CountDisputes(string transactionId)
    {
        using var scope = _factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        var id = Guid.Parse(transactionId);
        return await database.Disputes.CountAsync(item => item.TransactionId == id);
    }

    private HttpClient CreateClient(string environment = "Testing", string? customerId = null)
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            if (customerId is not null)
                builder.UseSetting("Demo:CustomerId", customerId);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FrozenTimeProvider(FixedInstant));
            });
        }).CreateClient();
        if (environment == "Development")
            TestIdentityPasswords.LoginAsync(client).GetAwaiter().GetResult();
        AddCsrfTokenAsync(client).GetAwaiter().GetResult();
        return client;
    }

    private static async Task AddCsrfTokenAsync(HttpClient client)
    {
        using var meResponse = await client.GetAsync("/api/v1/auth/me");
        using var meBody = JsonDocument.Parse(await meResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", meBody.RootElement.GetProperty("csrfToken").GetString());
    }

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private sealed class FrozenTimeProvider(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
