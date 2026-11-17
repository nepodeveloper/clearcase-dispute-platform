using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ClearCase.Api.IntegrationTests;

public sealed class SecurityHostingTests
{
    [Fact]
    public async Task ProductionHttpRedirectsAndDoesNotTrustUnconfiguredForwardedScheme()
    {
        using var app = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.25");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.StartsWith("https://", response.Headers.Location?.AbsoluteUri);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task ProductionHttpsReturnsHstsAndDefensiveHeadersWithoutExposingServerInternals()
    {
        using var app = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("max-age=", response.Headers.GetValues("Strict-Transport-Security").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("default-src 'none'",
            response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task ReadyFailsClosedWhenDatabaseIsNotPresent()
    {
        var missingPath = Path.Combine(Path.GetTempPath(),
            "clearcase-missing-ready-" + Guid.NewGuid().ToString("N") + ".db");
        using var app = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Persistence:DatabasePath"] = missingPath
                    }));
            });
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("not_ready", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("unavailable",
            json.RootElement.GetProperty("checks").GetProperty("database").GetString());
        Assert.DoesNotContain(missingPath, json.RootElement.GetRawText());
    }
}
