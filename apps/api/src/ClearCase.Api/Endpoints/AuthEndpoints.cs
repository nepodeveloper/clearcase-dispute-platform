using System.Security.Claims;
using ClearCase.Api.Authentication;
using ClearCase.Infrastructure.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace ClearCase.Api.Endpoints;

public static class AuthEndpoints
{
    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/auth/login", Login)
            .WithName("Login")
            .WithSummary("Sign in with a synthetic local user")
            .Accepts<LoginRequest>("application/json")
            .Produces<AuthSessionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting("auth-login");

        app.MapPost("/api/v1/auth/logout", Logout)
            .WithName("Logout")
            .WithSummary("Sign out and invalidate the current session")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireAuthorization()
            .RequireCsrfProtection();

        app.MapGet("/api/v1/auth/me", Me)
            .WithName("CurrentPrincipal")
            .WithSummary("Return the authenticated identity and role claims")
            .Produces<AuthSessionResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<AuthSessionResponse>, ProblemHttpResult>> Login(
        LoginRequest request,
        HttpContext context,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid login request",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "auth.invalidRequest"
                });
        }

        var signInResult = await signInManager.PasswordSignInAsync(
            request.Username.Trim(),
            request.Password,
            isPersistent: false,
            lockoutOnFailure: true);
        if (!signInResult.Succeeded)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid credentials",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "auth.invalidCredentials"
                });
        }

        var user = await userManager.FindByNameAsync(request.Username.Trim());
        if (user is null)
            throw new InvalidOperationException("Signed-in user record could not be loaded.");

        var refreshedPrincipal = await signInManager.CreateUserPrincipalAsync(user);
        var csrfToken = antiforgery.GetAndStoreTokens(context).RequestToken
                        ?? throw new InvalidOperationException("Anti-forgery token generation failed.");
        return TypedResults.Ok(AuthSessionResponse.From(refreshedPrincipal, csrfToken));
    }

    private static async Task<NoContent> Logout(
        SignInManager<ApplicationUser> signInManager,
        HttpContext context,
        IAntiforgery antiforgery)
    {
        await signInManager.SignOutAsync();
        antiforgery.SetCookieTokenAndHeader(context);
        return TypedResults.NoContent();
    }

    private static Ok<AuthSessionResponse> Me(HttpContext context, IAntiforgery antiforgery)
    {
        var csrfToken = antiforgery.GetAndStoreTokens(context).RequestToken
                        ?? throw new InvalidOperationException("Anti-forgery token generation failed.");
        return TypedResults.Ok(AuthSessionResponse.From(context.User, csrfToken));
    }
}

public sealed record LoginRequest(string Username, string Password);

public sealed record AuthSessionResponse(
    string Username,
    string ActorType,
    string[] Roles,
    string? CustomerId,
    string CsrfToken)
{
    public static AuthSessionResponse From(ClaimsPrincipal principal, string csrfToken)
    {
        var roles = principal.FindAll(ClaimTypes.Role).Select(item => item.Value).Distinct().ToArray();
        var actorType = principal.FindFirstValue(SyntheticIdentitySeeder.ActorTypeClaimType) ?? "unknown";
        var customerId = principal.FindFirstValue(SyntheticIdentitySeeder.CustomerIdClaimType);
        var username = principal.Identity?.Name
                       ?? principal.FindFirstValue(ClaimTypes.Name)
                       ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? "unknown";
        return new AuthSessionResponse(username, actorType, roles, customerId, csrfToken);
    }
}
