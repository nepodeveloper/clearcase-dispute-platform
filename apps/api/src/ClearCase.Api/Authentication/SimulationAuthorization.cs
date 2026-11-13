using System.Security.Cryptography;
using System.Text;

namespace ClearCase.Api.Authentication;

internal static class SimulationAuthorization
{
    public static bool IsEnabled(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Testing");

    public static void AddPolicy(WebApplicationBuilder builder, string policyName, string configurationKey) =>
        builder.Services.AddAuthorization(options => options.AddPolicy(policyName, policy =>
            policy.RequireAuthenticatedUser().RequireAssertion(context =>
            {
                var key = builder.Configuration[configurationKey];
                return !string.IsNullOrWhiteSpace(key) && context.Resource is HttpContext http
                    && http.Request.Headers["X-Simulation-Key"] is { Count: 1 } supplied
                    && CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(supplied.ToString()));
            })));
}
