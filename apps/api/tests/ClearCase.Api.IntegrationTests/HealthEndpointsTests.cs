using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ClearCase.Api.IntegrationTests;

public sealed class HealthEndpointsTests : IClassFixture<SqliteWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointsTests(SqliteWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task LiveEndpoint_ReturnsLiveStatus()
    {
        using var response = await _client.GetAsync("/health/live");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("live", body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ReadyEndpoint_ReportsApplicationStartup()
    {
        using var response = await _client.GetAsync("/health/ready");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ready", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "ready",
            body.RootElement.GetProperty("checks").GetProperty("application").GetString());
    }

    [Fact]
    public async Task RequestCorrelationId_IsReturnedInResponseHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-ID", "request-correlation-123");

        using var response = await _client.SendAsync(request);

        Assert.Equal("request-correlation-123", response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task DevelopmentCors_AllowsConfiguredAngularOrigin()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/health/live");
        request.Headers.Add("Origin", "http://localhost:4200");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        using var response = await _client.SendAsync(request);

        Assert.Equal(
            "http://localhost:4200",
            response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task Cors_IsNotEnabledOutsideDevelopment()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/health/live");
        request.Headers.Add("Origin", "http://localhost:4200");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        using var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task NotFound_UsesProblemDetailsWithCorrelationId()
    {
        using var response = await _client.GetAsync("/not-a-route");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(404, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(
            response.Headers.GetValues("X-Correlation-ID").Single(),
            body.RootElement.GetProperty("correlationId").GetString());
    }
}