using System.Net;
using System.Net.Http.Json;

namespace ClearCase.Api.IntegrationTests;

internal static class TestIdentityPasswords
{
    public static readonly IReadOnlyDictionary<string, string?> Seed = new Dictionary<string, string?>
    {
        ["Customer"] = NewPassword(),
        ["OtherCustomer"] = NewPassword(),
        ["Agent"] = NewPassword(),
        ["Supervisor"] = NewPassword()
    };

    public static string NewPassword() => $"Cc1!{Guid.NewGuid():N}";

    public static string For(string username) => Seed[username switch
    {
        "customer.demo" => "Customer",
        "customer.other" => "OtherCustomer",
        "agent.demo" => "Agent",
        "supervisor.demo" => "Supervisor",
        _ => throw new ArgumentOutOfRangeException(nameof(username))
    }]!;

    public static async Task LoginAsync(HttpClient client, string username = "customer.demo")
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { username, password = For(username) });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
