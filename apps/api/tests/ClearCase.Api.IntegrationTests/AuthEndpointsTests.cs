using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Claims;
using ClearCase.Api.Authentication;
using ClearCase.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClearCase.Api.IntegrationTests;

public sealed class AuthEndpointsTests : IClassFixture<SqliteWebApplicationFactory>
{
    private readonly SqliteWebApplicationFactory _factory;

    public AuthEndpointsTests(SqliteWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Me_CompatibilityPathPreservesExistingDemoCustomerInTesting()
    {
        using var client = CreateClient("Testing");
        using var response = await client.GetAsync("/api/v1/auth/me");
        using var body = await ReadJson(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("customer", body.RootElement.GetProperty("actorType").GetString());
        Assert.Equal("11111111-1111-1111-1111-111111111111", body.RootElement.GetProperty("customerId").GetString());
        Assert.Contains("Customer", body.RootElement.GetProperty("roles").EnumerateArray()
            .Select(item => item.GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("csrfToken").GetString()));
    }

    [Fact]
    public async Task Login_CustomerIdentityMapsToOwnedTransactions()
    {
        using var client = CreateClient("Testing");
        await LoginAsync(client, "customer.other");

        using var me = await ReadJson(await client.GetAsync("/api/v1/auth/me"));
        Assert.Equal("22222222-2222-2222-2222-222222222222", me.RootElement.GetProperty("customerId").GetString());

        using var list = await ReadJson(await client.GetAsync("/api/v1/transactions?pageSize=100"));
        Assert.Equal(1, list.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal("00000000-0000-0000-0000-000000000099",
            list.RootElement.GetProperty("items")[0].GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("agent.demo", "Agent")]
    [InlineData("supervisor.demo", "Supervisor")]
    public async Task Login_BankEmployeeClaimsAndCustomerResourceSeparation(
        string username,
        string expectedRole)
    {
        using var client = CreateClient("Testing");
        await LoginAsync(client, username);

        using var meResponse = await client.GetAsync("/api/v1/auth/me");
        using var me = await ReadJson(meResponse);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        Assert.Equal("bankEmployee", me.RootElement.GetProperty("actorType").GetString());
        Assert.Equal(JsonValueKind.Null, me.RootElement.GetProperty("customerId").ValueKind);
        Assert.Contains(expectedRole, me.RootElement.GetProperty("roles").EnumerateArray()
            .Select(item => item.GetString()));

        using var listResponse = await client.GetAsync("/api/v1/transactions");
        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);
    }

    [Fact]
    public async Task Login_InvalidPasswordAndRateLimitAreEnforced()
    {
        await using var isolatedFactory = new SqliteWebApplicationFactory();
        await isolatedFactory.InitializeAsync();
        using var client = CreateClient(isolatedFactory, "Testing");
        HttpStatusCode lastStatus = HttpStatusCode.OK;

        for (var attempt = 0; attempt < 6; attempt++)
        {
            using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
            {
                username = "customer.demo",
                password = "wrong-password"
            });
            lastStatus = response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, lastStatus);
    }

    [Fact]
    public async Task CompatibilityPath_DoesNotFallbackWhenIdentityCookieIsInvalid()
    {
        using var client = CreateClient("Testing");
        client.DefaultRequestHeaders.Add("Cookie", "clearcase.auth=invalid-cookie-value");

        using var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CompatibilityPath_DoesNotFallbackWhenIdentityCookieIsPresentButEmpty()
    {
        using var client = CreateClient("Testing");
        client.DefaultRequestHeaders.Add("Cookie", "clearcase.auth=");

        using var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_EndsCookieSession_WhenCompatibilityPathDisabled()
    {
        using var client = CreateClient("Testing", compatibilityPath: false);
        await LoginAsync(client, "customer.demo");

        var csrfToken = await ReadCsrfTokenAsync(client);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrfToken);

        using var logout = await client.PostAsync("/api/v1/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var meResponse = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);
    }

    [Fact]
    public async Task StateChangingRequest_WithoutCsrfToken_IsRejected()
    {
        using var client = CreateClient("Testing", compatibilityPath: false);
        await LoginAsync(client, "customer.demo");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");

        using var response = await client.PostAsJsonAsync("/api/v1/disputes", new
        {
            transactionId = "00000000-0000-0000-0000-000000000001",
            reasonCode = "unrecognisedTransaction",
            description = "I did not make this purchase at the store."
        });
        using var body = await ReadJson(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("auth.csrfInvalid", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Production_DisablesDevelopmentCompatibilityPath()
    {
        using var client = CreateClient("Production");
        using var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/auth/me")]
    [InlineData("/api/v1/transactions")]
    [InlineData("/api/v1/disputes")]
    public async Task Development_NeverUsesCompatibilityAuthentication(string path)
    {
        using var client = CreateClient("Development", compatibilityPath: true);
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LoginToken_ProtectsSubmissionWithoutAnAdditionalMeRequest()
    {
        using var client = CreateClient("Testing", compatibilityPath: false);
        await LoginAsync(client, "customer.demo");
        using var response = await client.PostAsJsonAsync("/api/v1/disputes", new
        {
            transactionId = "00000000-0000-0000-0000-000000000001",
            reasonCode = "unrecognisedTransaction",
            description = "Please investigate this synthetic card purchase."
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredIdentityCookie_CannotFallBackToSyntheticAuthentication()
    {
        using var host = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        using var client = host.CreateClient();
        var options = host.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var expired = new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "expired")], "cookie")),
            new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UnixEpoch },
            IdentityConstants.ApplicationScheme);
        client.DefaultRequestHeaders.Add("Cookie", $"clearcase.auth={options.TicketDataFormat.Protect(expired)}");

        using var response = await client.GetAsync("/api/v1/transactions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("customer.demo", false)]
    [InlineData("agent.demo", false)]
    [InlineData("supervisor.demo", true)]
    public async Task SupervisorPolicy_UsesAuthenticatedIdentityRoles(string username, bool allowed)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        var user = await manager.FindByNameAsync(username);
        var principal = await signIn.CreateUserPrincipalAsync(user!);

        var result = await scope.ServiceProvider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(principal, null, AuthenticationPolicies.SupervisorAccess);

        Assert.Equal(allowed, result.Succeeded);
    }

    [Fact]
    public async Task CookieCustomer_CannotReadAnotherCustomersRecords_AndMeIsNotCached()
    {
        using var owner = CreateClient("Testing", compatibilityPath: false);
        await LoginAsync(owner, "customer.demo");
        using var created = await owner.PostAsJsonAsync("/api/v1/disputes", new
        {
            transactionId = "00000000-0000-0000-0000-000000000006",
            reasonCode = "incorrectFee",
            description = "Please investigate this synthetic account fee."
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var body = await ReadJson(created);
        var caseId = body.RootElement.GetProperty("id").GetGuid();
        using var client = CreateClient("Testing", compatibilityPath: false);
        await LoginAsync(client, "customer.other");
        using var forbiddenRecord = await client.GetAsync("/api/v1/transactions/00000000-0000-0000-0000-000000000001");
        using var me = await client.GetAsync("/api/v1/auth/me");
        using var forbiddenCase = await client.GetAsync($"/api/v1/disputes/{caseId}");

        Assert.Equal(HttpStatusCode.NotFound, forbiddenRecord.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, forbiddenCase.StatusCode);
        Assert.True(me.Headers.CacheControl?.NoStore);
    }

    private HttpClient CreateClient(string environment, bool compatibilityPath = true) =>
        CreateClient(_factory, environment, compatibilityPath);

    private static HttpClient CreateClient(
        SqliteWebApplicationFactory factory,
        string environment,
        bool compatibilityPath = true)
    {
        var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Authentication:EnableDevelopmentCompatibilityPath"] = compatibilityPath.ToString()
                });
            });
        });
        return host.CreateClient();
    }

    private static async Task LoginAsync(HttpClient client, string username)
    {
        var password = TestIdentityPasswords.For(username);
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csrfToken = (await ReadJson(response)).RootElement.GetProperty("csrfToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(csrfToken));
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrfToken);
    }

    private static async Task<string> ReadCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/auth/me");
        using var body = await ReadJson(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return body.RootElement.GetProperty("csrfToken").GetString()
               ?? throw new InvalidOperationException("Expected csrfToken.");
    }

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
