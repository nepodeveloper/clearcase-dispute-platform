using System.Net;
using System.Text.Json;
using ClearCase.Application.DisputeEligibility;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClearCase.Api.IntegrationTests;

public sealed class DisputeEligibilityEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string CardId = "00000000-0000-0000-0000-000000000001";
    private const string DebitOrderId = "00000000-0000-0000-0000-000000000002";
    private const string BankChargeId = "00000000-0000-0000-0000-000000000003";
    private const string PendingCardId = "00000000-0000-0000-0000-000000000004";
    private const string HighAmountDebitOrderId = "00000000-0000-0000-0000-000000000005";
    private const string OtherCustomerId = "00000000-0000-0000-0000-000000000099";
    private static readonly DateTimeOffset FixedInstant =
        new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public DisputeEligibilityEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Theory]
    [InlineData("/api/v1/transactions/")]
    [InlineData("/v1/transactions/")]
    public async Task Get_OwnedTransaction_ReturnsEligibilityContract(string route)
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, route + CardId + "/dispute-eligibility");
        request.Headers.Add("X-Correlation-ID", "eligibility-contract");
        using var response = await client.SendAsync(request);
        using var body = await ReadJson(response);
        var result = body.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("eligibility-contract", response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Equal(CardId, result.GetProperty("transactionId").GetString());
        Assert.Equal("EligibleToSubmit", result.GetProperty("outcome").GetString());
        Assert.Empty(result.GetProperty("reasonCodes").EnumerateArray());
        Assert.Equal("This transaction can be submitted for review.", result.GetProperty("explanation").GetString());
        Assert.Equal("demo-v1", result.GetProperty("policyVersion").GetString());
        Assert.Equal("2026-11-12T10:00:00+00:00", result.GetProperty("evaluatedAt").GetString());
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData(DebitOrderId, "EligibleToSubmit")]
    [InlineData(BankChargeId, "EligibleToSubmit")]
    [InlineData(PendingCardId, "Ineligible")]
    [InlineData(HighAmountDebitOrderId, "RequiresManualReview")]
    public async Task Get_SelectsCategoryPolicyAndReturnsDecisionAsSuccess(string transactionId, string outcome)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(
            $"/api/v1/transactions/{transactionId}/dispute-eligibility");
        using var body = await ReadJson(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(outcome, body.RootElement.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Get_IneligibleDecisionMatchesDisplayedPendingStatusAndIsNotForbidden()
    {
        using var client = CreateClient();
        using var details = await client.GetAsync($"/api/v1/transactions/{PendingCardId}");
        using var detailsBody = await ReadJson(details);
        using var response = await client.GetAsync(
            $"/api/v1/transactions/{PendingCardId}/dispute-eligibility");
        using var eligibilityBody = await ReadJson(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("PendingAuthorization", detailsBody.RootElement.GetProperty("status").GetString());
        Assert.True(detailsBody.RootElement.GetProperty("isDisputable").GetBoolean());
        Assert.Equal("Ineligible", eligibilityBody.RootElement.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Get_OtherCustomerAndUnknownTransaction_ReturnSameNotFoundProblem()
    {
        using var client = CreateClient();
        using var otherRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/v1/transactions/{OtherCustomerId}/dispute-eligibility");
        otherRequest.Headers.Add("X-Customer-ID", "22222222-2222-2222-2222-222222222222");
        otherRequest.Headers.Add("X-Correlation-ID", "same-not-found");
        using var other = await client.SendAsync(otherRequest);
        using var missingRequest = new HttpRequestMessage(HttpMethod.Get,
            "/v1/transactions/00000000-0000-0000-0000-000000000100/dispute-eligibility");
        missingRequest.Headers.Add("X-Correlation-ID", "same-not-found");
        using var missing = await client.SendAsync(missingRequest);
        using var otherBody = await ReadJson(other);
        using var missingBody = await ReadJson(missing);

        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", other.Content.Headers.ContentType?.MediaType);
        Assert.Equal("transaction.notFound", otherBody.RootElement.GetProperty("code").GetString());
        var otherFields = otherBody.RootElement.EnumerateObject()
            .Where(property => property.Name != "traceId")
            .Select(property => (property.Name, Value: property.Value.GetRawText()))
            .OrderBy(property => property.Name).ToArray();
        var missingFields = missingBody.RootElement.EnumerateObject()
            .Where(property => property.Name != "traceId")
            .Select(property => (property.Name, Value: property.Value.GetRawText()))
            .OrderBy(property => property.Name).ToArray();
        Assert.Equal(otherFields, missingFields);
    }

    [Fact]
    public async Task Get_InvalidId_ReturnsProblemDetailsAndCorrelationId()
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "/v1/transactions/not-a-guid/dispute-eligibility");
        request.Headers.Add("X-Correlation-ID", "invalid-eligibility");
        using var response = await client.SendAsync(request);
        using var body = await ReadJson(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("invalid-eligibility", response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Equal("invalid-eligibility", body.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal("/id", body.RootElement.GetProperty("errors")[0].GetProperty("pointer").GetString());
    }

    [Fact]
    public async Task Get_RequiresAuthenticationOutsideDemoEnvironments()
    {
        using var client = CreateClient("Production");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/transactions/{CardId}/dispute-eligibility");
        request.Headers.Add("X-Customer-ID", "11111111-1111-1111-1111-111111111111");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DevelopmentOpenApi_DescribesEligibilityRoutesAndResponse()
    {
        using var client = CreateClient("Development");
        using var response = await client.GetAsync("/openapi/v1.json");
        using var body = await ReadJson(response);
        var paths = body.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/v1/transactions/{id}/dispute-eligibility", out _));
        var operation = paths.GetProperty("/api/v1/transactions/{id}/dispute-eligibility").GetProperty("get");
        Assert.True(operation.GetProperty("responses").TryGetProperty("200", out _));
        Assert.True(operation.GetProperty("responses").TryGetProperty("404", out _));
        Assert.True(operation.GetProperty("responses").TryGetProperty("500", out _));
    }

    private HttpClient CreateClient(string environment = "Testing") =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FrozenTimeProvider(FixedInstant));
            });
        }).CreateClient();

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private sealed class FrozenTimeProvider(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
