using System.Security.Claims;
using System.Text.Encodings.Web;
using ClearCase.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ClearCase.Api.Authentication;

public sealed class CompositeAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration,
    IHostEnvironment environment) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var applicationResult = await Context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (applicationResult.Succeeded)
            return applicationResult;

        if (!IsDevelopmentCompatibilityEnabled(configuration, environment))
            return AuthenticateResult.NoResult();

        if (!Guid.TryParse(configuration["Demo:CustomerId"], out var customerId))
        {
            throw new InvalidOperationException("Demo:CustomerId must be a configured synthetic customer GUID.");
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "33333333-3333-3333-3333-333333333331"),
                new Claim(ClaimTypes.Name, "customer.demo"),
                new Claim(ClaimTypes.Role, SyntheticIdentitySeeder.CustomerRole),
                new Claim(SyntheticIdentitySeeder.CustomerIdClaimType, customerId.ToString()),
                new Claim(SyntheticIdentitySeeder.ActorTypeClaimType, SyntheticIdentitySeeder.ActorTypeCustomer)
            ],
            Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    internal static bool IsDevelopmentCompatibilityEnabled(IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            return false;

        return bool.TryParse(configuration["Authentication:EnableDevelopmentCompatibilityPath"], out var enabled)
            ? enabled
            : true;
    }
}
