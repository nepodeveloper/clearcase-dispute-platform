using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClearCase.Application.DisputeEligibility;
using ClearCase.Domain.Transactions;
using ClearCase.Infrastructure.Transactions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClearCase.Api.IntegrationTests;

public sealed class DisputeEndpointsTests : IClassFixture<SqliteWebApplicationFactory>
{
    private const string CardId = "00000000-0000-0000-0000-000000000001";
    private const string BankChargeId = "00000000-0000-0000-0000-000000000003";
    private const string PendingCardId = "00000000-0000-0000-0000-000000000004";
    private const string HighAmountDebitOrderId = "00000000-0000-0000-0000-000000000005";
    private const string DuplicateCardId = "00000000-0000-0000-0000-000000000007";
    private const string OtherCustomersTransactionId = "00000000-0000-0000-0000-000000000099";
    private const string ValidDescription = "I did not make this purchase at the store.";
    private static readonly Guid DemoCustomer = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset FixedInstant = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteWebApplicationFactory _factory;

    public DisputeEndpointsTests(SqliteWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Post_EligibleTransaction_Returns201AndPersistsCaseForAuthenticatedCustomer()
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/disputes")
        {
            Content = JsonContent.Create(new
            {
                transactionId = CardId,
                reasonCode = "unrecognisedTransaction",
                description = "  " + ValidDescription + "  ",
                customerId = "22222222-2222-2222-2222-222222222222"
            })
        };
        request.Headers.Add("X-Correlation-ID", "submit-dispute");
        using var response = await client.SendAsync(request);
        using var body = await ReadJson(response);
        var created = body.RootElement;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("submit-dispute", response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.True(response.Headers.CacheControl?.NoStore);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/disputes/{id}", response.Headers.Location?.OriginalString);
        Assert.Matches("^DSP-[0-9A-F]{12}$", created.GetProperty("reference").GetString());
        Assert.Equal(CardId, created.GetProperty("transactionId").GetString());
        Assert.Equal("CardPurchase", created.GetProperty("category").GetString());
        Assert.Equal("unrecognisedTransaction", created.GetProperty("reasonCode").GetString());
        Assert.Equal(ValidDescription, created.GetProperty("description").GetString());
        Assert.Equal("Submitted", created.GetProperty("caseStatus").GetString());
        Assert.Equal("Pending", created.GetProperty("decisionStatus").GetString());
        Assert.Equal("NotInitiated", created.GetProperty("financialStatus").GetString());
        Assert.Equal("2026-11-12T10:00:00+00:00", created.GetProperty("createdAt").GetString());
        var timelineEvent = Assert.Single(created.GetProperty("timeline").EnumerateArray());
        Assert.Equal(1, timelineEvent.GetProperty("sequenceNumber").GetInt32());
        Assert.Equal("DisputeSubmitted", timelineEvent.GetProperty("eventType").GetString());

        using var fetched = await client.GetAsync($"/v1/disputes/{id}");
        using var fetchedBody = await ReadJson(fetched);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal(created.GetProperty("reference").GetString(),
            fetchedBody.RootElement.GetProperty("reference").GetString());
        Assert.Single(fetchedBody.RootElement.GetProperty("timeline").EnumerateArray());

        using var scope = _factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        var stored = await database.Disputes.AsNoTracking().SingleAsync(item => item.Id == id);
        Assert.Equal(DemoCustomer, stored.CustomerId);
        Assert.Equal(1, await database.DisputeEvents.CountAsync(item => item.DisputeId == id));
    }

    [Fact]
    public async Task Post_HighAmountDebitOrder_RoutesToManualReview()
    {
        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync("/v1/disputes", new
        {
            transactionId = HighAmountDebitOrderId,
            reasonCode = "unauthorisedDebitOrder",
            description = "I never signed up for this insurance debit order."
        });
        using var body = await ReadJson(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.StartsWith("/v1/disputes/", response.Headers.Location?.OriginalString);
        Assert.Equal("ManualReview", body.RootElement.GetProperty("caseStatus").GetString());
        Assert.Equal("NotInitiated", body.RootElement.GetProperty("financialStatus").GetString());
        Assert.Equal("ManualReview",
            body.RootElement.GetProperty("timeline")[0].GetProperty("caseStatus").GetString());
    }

    [Fact]
    public async Task Post_DebiCheckAuthorizationComplaint_CreatesInvestigationWithoutFinancialAction()
    {
        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/disputes", new
        {
            transactionId = "00000000-0000-0000-0000-000000000011",
            reasonCode = "unauthorisedDebitOrder",
            description = "I dispute the authorization recorded for this mandate."
        });
        using var body = await ReadJson(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("ManualReview", body.RootElement.GetProperty("caseStatus").GetString());
        Assert.Equal("Pending", body.RootElement.GetProperty("decisionStatus").GetString());
        Assert.Equal("NotInitiated", body.RootElement.GetProperty("financialStatus").GetString());
        Assert.Equal("unauthorisedDebitOrder", body.RootElement.GetProperty("reasonCode").GetString());
        Assert.Single(body.RootElement.GetProperty("timeline").EnumerateArray());
    }

    [Fact]
    public async Task Post_RechecksMandateAfterEligibilityRead_BeforeCreatingCase()
    {
        var reader = new ChangingMandateReader();
        using var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FrozenTimeProvider(FixedInstant));
                services.RemoveAll<IDebitOrderMandateReader>();
                services.AddSingleton<IDebitOrderMandateReader>(reader);
            });
        }).CreateClient();
        await AddCsrfTokenAsync(client);
        const string transactionId = "00000000-0000-0000-0000-000000000002";
        using var eligibilityResponse = await client.GetAsync(
            $"/api/v1/transactions/{transactionId}/dispute-eligibility");
        using var eligibility = await ReadJson(eligibilityResponse);
        Assert.Equal("EligibleToSubmit", eligibility.RootElement.GetProperty("outcome").GetString());
        reader.MandateMissing = true;

        using var response = await client.PostAsJsonAsync("/api/v1/disputes", new
        {
            transactionId,
            reasonCode = "unauthorisedDebitOrder",
            description = "I dispute the authorisation for this collection."
        });
        using var body = await ReadJson(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("ManualReview", body.RootElement.GetProperty("caseStatus").GetString());
        Assert.Equal("NotInitiated", body.RootElement.GetProperty("financialStatus").GetString());
        Assert.Equal(2, reader.Calls);
        using var scope = _factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        var stored = await database.Disputes.SingleAsync(item => item.Id == body.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("proposed-debit-order-2026-04-13-v2", stored.EligibilityPolicyVersion);
    }

    [Fact]
    public async Task Post_IneligibleTransaction_Returns422AndPersistsNothing()
    {
        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/disputes", new
        {
            transactionId = PendingCardId,
            reasonCode = "unrecognisedTransaction",
            description = ValidDescription
        });
        using var body = await ReadJson(response);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("dispute.transactionIneligible", body.RootElement.GetProperty("code").GetString());
        Assert.NotEmpty(body.RootElement.GetProperty("reasonCodes").EnumerateArray());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("detail").GetString()));
        Assert.Equal(0, await CountDisputes(PendingCardId));
    }

    [Fact]
    public async Task Post_OtherCustomersTransaction_LooksIdenticalToUnknownTransaction()
    {
        using var client = CreateClient();
        using var other = await client.PostAsJsonAsync("/api/v1/disputes", new
        {
            transactionId = OtherCustomersTransactionId,
            reasonCode = "unrecognisedTransaction",
            description = ValidDescription
        });
        using var unknown = await client.PostAsJsonAsync("/api/v1/disputes", new
        {
            transactionId = "00000000-0000-0000-0000-000000000100",
            reasonCode = "unrecognisedTransaction",
            description = ValidDescription
        });
        using var otherBody = await ReadJson(other);
        using var unknownBody = await ReadJson(unknown);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, other.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);
        Assert.Equal("dispute.transactionNotFound", otherBody.RootElement.GetProperty("code").GetString());
        Assert.Equal(otherBody.RootElement.GetProperty("errors").GetRawText(),
            unknownBody.RootElement.GetProperty("errors").GetRawText());
        Assert.Equal(0, await CountDisputes(OtherCustomersTransactionId));
    }

    [Fact]
    public async Task Get_OtherCustomersDispute_Returns404()
    {
        var otherDisputeId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
            await database.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Disputes" ("Id", "Reference", "CustomerId", "TransactionId", "Category",
                 "ReasonCode", "Description", "CaseStatus", "DecisionStatus", "FinancialStatus",
                 "EligibilityOutcome", "EligibilityPolicyVersion", "CreatedAt")
                VALUES ({otherDisputeId}, 'DSP-OTHER0000001', {Guid.Parse("22222222-2222-2222-2222-222222222222")},
                 {Guid.Parse(OtherCustomersTransactionId)}, 'CardPurchase', 'unrecognisedTransaction',
                 'Other customer private description.', 'Submitted', 'Pending', 'NotInitiated',
                 'EligibleToSubmit', 'demo-v1', {FixedInstant.UtcTicks})
                """);
        }

        using var client = CreateClient();
        using var other = await client.GetAsync($"/api/v1/disputes/{otherDisputeId}");
        using var unknown = await client.GetAsync($"/api/v1/disputes/{Guid.NewGuid()}");
        using var otherBody = await ReadJson(other);

        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("dispute.notFound", otherBody.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("private", otherBody.RootElement.GetRawText());
    }

    [Fact]
    public async Task Post_SecondOpenDisputeForSameTransaction_Returns409()
    {
        using var client = CreateClient();
        var payload = new
        {
            transactionId = DuplicateCardId,
            reasonCode = "duplicateCharge",
            description = "I was charged twice for the same purchase."
        };
        using var first = await client.PostAsJsonAsync("/api/v1/disputes", payload);
        using var second = await client.PostAsJsonAsync("/api/v1/disputes", payload);
        using var body = await ReadJson(second);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("dispute.activeCaseExists", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(1, await CountDisputes(DuplicateCardId));
    }

    [Fact]
    public async Task Post_InvalidFields_Returns400WithPointers()
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/disputes")
        {
            Content = JsonContent.Create(new { transactionId = "not-a-guid", reasonCode = " ", description = "short" })
        };
        request.Headers.Add("X-Correlation-ID", "invalid-dispute");
        using var response = await client.SendAsync(request);
        using var body = await ReadJson(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("dispute.invalidRequest", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("invalid-dispute", body.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal(new[] { "/transactionId", "/reasonCode", "/description" },
            body.RootElement.GetProperty("errors").EnumerateArray()
                .Select(error => error.GetProperty("pointer").GetString()).ToArray());
    }

    [Fact]
    public async Task Post_ReasonFromAnotherCategory_Returns422()
    {
        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/disputes", new
        {
            transactionId = BankChargeId,
            reasonCode = "unauthorisedDebitOrder",
            description = "This fee should not have been charged."
        });
        using var body = await ReadJson(response);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("dispute.invalidReason", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("/reasonCode", body.RootElement.GetProperty("errors")[0].GetProperty("pointer").GetString());
        Assert.Equal(0, await CountDisputes(BankChargeId));
    }

    [Fact]
    public async Task Post_MalformedJson_Returns400ProblemDetails()
    {
        using var client = CreateClient();
        using var response = await client.PostAsync("/api/v1/disputes",
            new StringContent("{ not json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("POST", "/api/v1/disputes")]
    [InlineData("GET", "/v1/disputes/00000000-0000-0000-0000-000000000001")]
    public async Task Production_RequiresAuthentication(string method, string route)
    {
        using var client = CreateClient("Production", includeCsrfToken: false);
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method == "POST")
            request.Content = JsonContent.Create(new
            {
                transactionId = CardId, reasonCode = "duplicateCharge", description = ValidDescription
            });
        request.Headers.Add("X-Customer-ID", DemoCustomer.ToString());
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Eligibility_ListsReasonsOnlyWhenSubmissionIsPossible()
    {
        using var client = CreateClient();
        using var eligible = await ReadJson(await client.GetAsync($"/api/v1/transactions/{BankChargeId}/dispute-eligibility"));
        using var ineligible = await ReadJson(await client.GetAsync($"/api/v1/transactions/{PendingCardId}/dispute-eligibility"));

        Assert.Equal(new[] { "incorrectFee", "duplicateFee", "feeNotAgreed" },
            eligible.RootElement.GetProperty("availableReasons").EnumerateArray()
                .Select(reason => reason.GetProperty("code").GetString()).ToArray());
        Assert.Empty(ineligible.RootElement.GetProperty("availableReasons").EnumerateArray());
    }

    [Fact]
    public async Task DevelopmentOpenApi_DescribesDisputeRoutes()
    {
        using var client = CreateClient("Development");
        using var body = await ReadJson(await client.GetAsync("/openapi/v1.json"));
        var paths = body.RootElement.GetProperty("paths");

        var create = paths.GetProperty("/api/v1/disputes").GetProperty("post");
        foreach (var status in new[] { "201", "400", "401", "409", "422" })
            Assert.True(create.GetProperty("responses").TryGetProperty(status, out _), status);
        Assert.True(paths.GetProperty("/api/v1/disputes/{id}").TryGetProperty("get", out _));
        Assert.True(paths.TryGetProperty("/v1/disputes", out _));
        Assert.True(paths.TryGetProperty("/v1/disputes/{id}", out _));
    }

    private async Task<int> CountDisputes(string transactionId)
    {
        using var scope = _factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        var id = Guid.Parse(transactionId);
        return await database.Disputes.CountAsync(item => item.TransactionId == id);
    }

    private HttpClient CreateClient(string environment = "Testing", bool includeCsrfToken = true)
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FrozenTimeProvider(FixedInstant));
            });
        }).CreateClient();
        if (includeCsrfToken)
            AddCsrfTokenAsync(client).GetAwaiter().GetResult();

        return client;
    }

    private static async Task AddCsrfTokenAsync(HttpClient client)
    {
        using var meResponse = await client.GetAsync("/api/v1/auth/me");
        using var meBody = JsonDocument.Parse(await meResponse.Content.ReadAsStringAsync());
        var csrfToken = meBody.RootElement.GetProperty("csrfToken").GetString();
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(csrfToken));
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrfToken);
    }

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private sealed class FrozenTimeProvider(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }

    private sealed class ChangingMandateReader : IDebitOrderMandateReader
    {
        public bool MandateMissing { get; set; }
        public int Calls { get; private set; }

        public Task<DebitOrderMandate?> ReadAsync(
            Guid customerId, Guid transactionId, CancellationToken cancellationToken)
        {
            Calls++;
            return MandateMissing ? Task.FromResult<DebitOrderMandate?>(null)
                : new MockDebitOrderMandateReader().ReadAsync(customerId, transactionId, cancellationToken);
        }
    }
}
