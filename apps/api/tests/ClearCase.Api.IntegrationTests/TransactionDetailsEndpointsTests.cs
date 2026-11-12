using System.Net;
using System.Text.Json;
using ClearCase.Api.Transactions;
using ClearCase.Application.Transactions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClearCase.Api.IntegrationTests;

public sealed class TransactionDetailsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string OwnedId = "00000000-0000-0000-0000-000000000001";
    private readonly WebApplicationFactory<Program> _factory;

    public TransactionDetailsEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Theory]
    [InlineData("/api/v1/transactions/")]
    [InlineData("/v1/transactions/")]
    public async Task Get_OwnedTransaction_ReturnsDetails(string route)
    {
        using var client = CreateClient("Testing");
        using var response = await client.GetAsync(route + OwnedId);
        using var body = await ReadJson(response);
        var transaction = body.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OwnedId, transaction.GetProperty("id").GetString());
        Assert.Equal("CardPurchase", transaction.GetProperty("transactionType").GetString());
        Assert.Equal("Woolworths", transaction.GetProperty("description").GetString());
        Assert.Equal("Woolworths", transaction.GetProperty("merchantName").GetString());
        Assert.Equal(JsonValueKind.String, transaction.GetProperty("amount").ValueKind);
        Assert.Equal("750.00", transaction.GetProperty("amount").GetString());
        Assert.Equal("ZAR", transaction.GetProperty("currency").GetString());
        Assert.Equal("2026-11-10T08:30:00+00:00", transaction.GetProperty("transactionDate").GetString());
        Assert.Equal("Posted", transaction.GetProperty("status").GetString());
        Assert.Equal("TXN-2026-000001", transaction.GetProperty("reference").GetString());
        Assert.True(transaction.GetProperty("isDisputable").GetBoolean());
        Assert.Equal(
            new[] { "amount", "currency", "description", "id", "isDisputable", "merchantName",
                "reference", "status", "transactionDate", "transactionType" },
            transaction.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Get_OptionalMerchant_IsOmittedWhenUnavailable()
    {
        using var client = CreateClient("Testing");
        using var response = await client.GetAsync("/v1/transactions/00000000-0000-0000-0000-000000000003");
        using var body = await ReadJson(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(body.RootElement.TryGetProperty("merchantName", out _));
    }

    [Fact]
    public async Task Get_UnknownAndOtherCustomersTransactions_AreIndistinguishable()
    {
        using var client = CreateClient("Testing");
        using var otherRequest = new HttpRequestMessage(HttpMethod.Get, "/v1/transactions/00000000-0000-0000-0000-000000000099?customerId=22222222-2222-2222-2222-222222222222");
        otherRequest.Headers.Add("X-Customer-ID", "22222222-2222-2222-2222-222222222222");
        otherRequest.Headers.Add("X-Correlation-ID", "detail-test");
        using var unknownRequest = new HttpRequestMessage(HttpMethod.Get, "/v1/transactions/00000000-0000-0000-0000-000000000100");
        unknownRequest.Headers.Add("X-Correlation-ID", "detail-test");
        using var other = await client.SendAsync(otherRequest);
        using var unknown = await client.SendAsync(unknownRequest);
        using var body = await ReadJson(other);
        using var unknownBody = await ReadJson(unknown);

        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("application/problem+json", other.Content.Headers.ContentType?.MediaType);
        Assert.Equal("detail-test", other.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Equal("detail-test", body.RootElement.GetProperty("correlationId").GetString());
        var otherFields = body.RootElement.EnumerateObject()
            .Where(property => property.Name != "traceId")
            .Select(property => (property.Name, Value: property.Value.GetRawText()))
            .OrderBy(property => property.Name).ToArray();
        var unknownFields = unknownBody.RootElement.EnumerateObject()
            .Where(property => property.Name != "traceId")
            .Select(property => (property.Name, Value: property.Value.GetRawText()))
            .OrderBy(property => property.Name).ToArray();
        Assert.Equal(otherFields, unknownFields);
    }

    [Fact]
    public async Task Get_UsesConfiguredCustomerForOwnership()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Demo:CustomerId"] = "22222222-2222-2222-2222-222222222222" }));
        });
        using var client = factory.CreateClient();
        using var owned = await client.GetAsync("/v1/transactions/00000000-0000-0000-0000-000000000099");
        using var other = await client.GetAsync("/v1/transactions/" + OwnedId);

        Assert.Equal(HttpStatusCode.OK, owned.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
    }

    [Fact]
    public async Task Reader_StopsWhenCancellationWasRequested()
    {
        var reader = new InMemoryTransactionReader();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            reader.ReadByIdAsync(Guid.Empty, Guid.Empty, cancellation.Token));
    }

    [Fact]
    public async Task Get_InvalidId_ReturnsProblemDetailsWithCorrelationId()
    {
        using var client = CreateClient("Testing");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/transactions/not-a-guid");
        request.Headers.Add("X-Correlation-ID", "invalid-detail");
        using var response = await client.SendAsync(request);
        using var body = await ReadJson(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("invalid-detail", response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Equal("invalid-detail", body.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal("/id", body.RootElement.GetProperty("errors")[0].GetProperty("pointer").GetString());
    }

    [Theory]
    [InlineData("/api/v1/transactions/")]
    [InlineData("/v1/transactions/")]
    public async Task Production_RequiresAuthentication(string route)
    {
        using var client = CreateClient("Production");
        using var request = new HttpRequestMessage(HttpMethod.Get, route + OwnedId);
        request.Headers.Add("X-Customer-ID", "11111111-1111-1111-1111-111111111111");
        request.Headers.Add("X-Correlation-ID", "unauthenticated-detail");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthenticated-detail", response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task Get_UnexpectedFailure_DoesNotExposeInternalDetails()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGetTransactionDetailsHandler>();
                services.AddScoped<IGetTransactionDetailsHandler, FailingDetailsHandler>();
            });
        });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/transactions/" + OwnedId);
        request.Headers.Add("X-Correlation-ID", "details-error");
        using var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(content);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("details-error", response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Equal("details-error", body.RootElement.GetProperty("correlationId").GetString());
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.DoesNotContain("internal-test-sentinel", content);
    }

    [Fact]
    public async Task DevelopmentOpenApi_DescribesDetailRoutesAndResponses()
    {
        using var client = CreateClient("Development");
        using var response = await client.GetAsync("/openapi/v1.json");
        using var body = await ReadJson(response);
        var paths = body.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/v1/transactions/{id}", out _));
        var responses = paths.GetProperty("/api/v1/transactions/{id}").GetProperty("get").GetProperty("responses");
        foreach (var status in new[] { "200", "400", "401", "404" })
            Assert.True(responses.TryGetProperty(status, out _), $"Missing response {status}");
        var schema = body.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("TransactionDetailsResponse");
        Assert.Equal("string", schema.GetProperty("properties").GetProperty("amount").GetProperty("type").GetString());
    }

    private HttpClient CreateClient(string environment) =>
        _factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment)).CreateClient();

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private sealed class FailingDetailsHandler : IGetTransactionDetailsHandler
    {
        public Task<TransactionDetails?> HandleAsync(Guid transactionId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("internal-test-sentinel");
    }
}
