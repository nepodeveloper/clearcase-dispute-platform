using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ClearCase.Api.IntegrationTests;

public sealed class TransactionEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TransactionEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task List_UsesDemoIdentityAndCannotBeSwitchedByRequest()
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/transactions?pageSize=100&customerId=other");
        request.Headers.Add("X-Customer-ID", "other");
        using var response = await client.SendAsync(request);
        using var body = await ReadJson(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(30, body.RootElement.GetProperty("totalCount").GetInt32());
        Assert.DoesNotContain(body.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "00000000-0000-0000-0000-000000000099");
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task List_ScopesDataToTheConfiguredAuthenticatedCustomer()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Demo:CustomerId"] = "22222222-2222-2222-2222-222222222222" }));
        });
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/v1/transactions");
        using var body = await ReadJson(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = body.RootElement.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("00000000-0000-0000-0000-000000000099", items[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task List_SortsBeforePaginatingAndSupportsBothRoutes()
    {
        using var client = CreateClient();
        using var firstResponse = await client.GetAsync("/api/v1/transactions?pageSize=2");
        using var secondResponse = await client.GetAsync("/v1/transactions?pageSize=2&pageNumber=2");
        using var first = await ReadJson(firstResponse);
        using var second = await ReadJson(secondResponse);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(30, first.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(15, first.RootElement.GetProperty("totalPages").GetInt32());
        Assert.True(first.RootElement.GetProperty("hasNextPage").GetBoolean());
        var firstItems = first.RootElement.GetProperty("items");
        var secondItems = second.RootElement.GetProperty("items");
        Assert.Equal("00000000-0000-0000-0000-000000000001", firstItems[0].GetProperty("id").GetString());
        Assert.Equal("00000000-0000-0000-0000-000000000002", firstItems[1].GetProperty("id").GetString());
        Assert.Equal("00000000-0000-0000-0000-000000000003", secondItems[0].GetProperty("id").GetString());
        Assert.Equal("2026-11-10T08:30:00+00:00", firstItems[0].GetProperty("transactionDate").GetString());
        Assert.Equal(JsonValueKind.String, firstItems[0].GetProperty("amount").ValueKind);
    }

    [Fact]
    public async Task List_DefaultsAndTypesAreDocumentedInTheData()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/v1/transactions?pageSize=100");
        using var body = await ReadJson(response);
        var root = body.RootElement;
        var types = root.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("transactionType").GetString()).Distinct().Order().ToArray();
        Assert.Equal(new[] { "BankCharge", "CardPurchase", "DebitOrder" }, types);

        using var defaultsResponse = await client.GetAsync("/v1/transactions");
        using var defaults = await ReadJson(defaultsResponse);
        Assert.Equal(1, defaults.RootElement.GetProperty("pageNumber").GetInt32());
        Assert.Equal(20, defaults.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(20, defaults.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task DevelopmentOpenApi_DescribesBothRoutesAndQueryParameters()
    {
        using var client = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development")).CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        using var body = await ReadJson(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var paths = body.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/v1/transactions", out _));
        var parameters = paths.GetProperty("/api/v1/transactions").GetProperty("get").GetProperty("parameters");
        var names = parameters.EnumerateArray().Select(parameter => parameter.GetProperty("name").GetString()).ToArray();
        Assert.Contains("pageNumber", names);
        Assert.Contains("pageSize", names);
        Assert.Contains("transactionType", names);
        Assert.Contains("fromDate", names);
        Assert.Contains("toDate", names);
    }

    [Fact]
    public async Task List_UsesIdToBreakDateTiesAndHandlesLargePageNumbers()
    {
        using var client = CreateClient();
        using var lastResponse = await client.GetAsync("/v1/transactions?pageSize=2&pageNumber=15");
        using var last = await ReadJson(lastResponse);
        var items = last.RootElement.GetProperty("items");
        Assert.Equal(items[0].GetProperty("transactionDate").GetString(), items[1].GetProperty("transactionDate").GetString());
        Assert.Equal("00000000-0000-0000-0000-000000000029", items[0].GetProperty("id").GetString());
        Assert.Equal("00000000-0000-0000-0000-000000000030", items[1].GetProperty("id").GetString());

        using var distantResponse = await client.GetAsync("/v1/transactions?pageSize=100&pageNumber=2147483647");
        using var distant = await ReadJson(distantResponse);
        Assert.Equal(HttpStatusCode.OK, distantResponse.StatusCode);
        Assert.Equal(0, distant.RootElement.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData("?transactionType=DebitOrder", 10)]
    [InlineData("?fromDate=2026-11-08", 3)]
    [InlineData("?toDate=2026-10-14", 3)]
    [InlineData("?transactionType=DebitOrder&fromDate=2026-11-05&toDate=2026-11-09", 2)]
    public async Task List_FiltersBeforePagination(string query, int expected)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/v1/transactions" + query + "&pageSize=1");
        using var body = await ReadJson(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, body.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("pageSize=abc")]
    [InlineData("pageSize=")]
    [InlineData("pageNumber=0")]
    [InlineData("transactionType=unknown")]
    [InlineData("transactionType=1")]
    [InlineData("fromDate=invalid")]
    [InlineData("fromDate=2026-11-10&toDate=2026-11-09")]
    public async Task List_InvalidQuery_ReturnsProblemDetails(string query)
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/transactions?" + query);
        request.Headers.Add("X-Correlation-ID", "test-correlation");
        using var response = await client.SendAsync(request);
        using var body = await ReadJson(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("test-correlation", response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Equal("test-correlation", body.RootElement.GetProperty("correlationId").GetString());
        var error = body.RootElement.GetProperty("errors")[0];
        Assert.StartsWith("/", error.GetProperty("pointer").GetString());
        Assert.False(string.IsNullOrEmpty(error.GetProperty("code").GetString()));
    }

    [Fact]
    public async Task List_EmptyResult_ReturnsArray()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/v1/transactions?fromDate=2030-01-01");
        using var body = await ReadJson(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(0, body.RootElement.GetProperty("totalPages").GetInt32());
        Assert.False(body.RootElement.GetProperty("hasNextPage").GetBoolean());
    }

    [Theory]
    [InlineData("/v1/transactions")]
    [InlineData("/api/v1/transactions")]
    public async Task Production_DoesNotAuthenticateDemoCustomer(string path)
    {
        using var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Demo:CustomerId"] = "11111111-1111-1111-1111-111111111111" }));
        }).CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Customer-ID", "demo");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private HttpClient CreateClient() =>
        _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing")).CreateClient();

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
