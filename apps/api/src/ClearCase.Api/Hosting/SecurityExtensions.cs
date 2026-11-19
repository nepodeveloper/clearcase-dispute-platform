using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using ClearCase.Api.Authentication;
using ClearCase.Api.Middleware;
using ClearCase.Infrastructure.Authentication;
using ClearCase.Infrastructure.Transactions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using IPNetwork = System.Net.IPNetwork;

namespace ClearCase.Api.Hosting;

public static class SecurityExtensions
{
    public static string[] ReadTrustedProxyAddresses(this IConfiguration configuration)
    {
        var addresses = configuration.GetSection("Security:TrustedProxyIps").Get<string[]>() ?? [];
        foreach (var address in addresses)
            if (!IPAddress.TryParse(address, out _))
                throw new InvalidOperationException("Security:TrustedProxyIps must contain literal trusted IP addresses.");
        return addresses;
    }

    /// <summary>CIDR ranges (for example a cluster's ingress-controller pod range) trusted to set forwarded headers.</summary>
    public static IPNetwork[] ReadTrustedProxyNetworks(this IConfiguration configuration)
    {
        var networks = configuration.GetSection("Security:TrustedProxyNetworks").Get<string[]>() ?? [];
        return networks.Select(network => IPNetwork.TryParse(network, out var parsed)
                ? parsed
                : throw new InvalidOperationException("Security:TrustedProxyNetworks must contain CIDR ranges such as 10.0.0.0/8."))
            .ToArray();
    }

    public static bool HasTrustedProxies(this IConfiguration configuration) =>
        configuration.ReadTrustedProxyAddresses().Length > 0 || configuration.ReadTrustedProxyNetworks().Length > 0;

    public static WebApplicationBuilder AddClearCaseSecurity(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var relaxedCookies = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
        var cookieSecurePolicy = relaxedCookies ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        // The synthetic-customer fallback exists for automated tests only; refuse to start with it anywhere else.
        if (builder.Configuration.GetValue<bool>("Authentication:EnableDevelopmentCompatibilityPath")
            && !builder.Environment.IsEnvironment("Testing"))
            throw new InvalidOperationException(
                "Authentication:EnableDevelopmentCompatibilityPath may only be enabled in the Testing environment.");

        // HTTP proxy headers are trusted ONLY from explicitly allow-listed ingress IPs.
        // Without a configured trusted proxy, forwarded client IP/scheme headers are ignored.
        var trustedProxyAddresses = builder.Configuration.ReadTrustedProxyAddresses();
        var trustedProxyNetworks = builder.Configuration.ReadTrustedProxyNetworks();
        if (trustedProxyAddresses.Length > 0 || trustedProxyNetworks.Length > 0)
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;
                options.KnownProxies.Clear();
                options.KnownIPNetworks.Clear();
                foreach (var address in trustedProxyAddresses)
                    options.KnownProxies.Add(IPAddress.Parse(address));
                foreach (var network in trustedProxyNetworks)
                    options.KnownIPNetworks.Add(network);
            });
        services.AddHttpsRedirection(options =>
        {
            options.HttpsPort = builder.Configuration.GetValue<int?>("Security:HttpsPort") ?? 443;
            options.RedirectStatusCode = StatusCodes.Status307TemporaryRedirect;
        });
        services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(365);
            options.IncludeSubDomains = false;
            options.Preload = false;
        });
        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.User.RequireUniqueEmail = false;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.Lockout.MaxFailedAccessAttempts = 5;
            })
            .AddEntityFrameworkStores<TransactionDbContext>()
            .AddDefaultTokenProviders();
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "clearcase.auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = cookieSecurePolicy;
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        });
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Composite";
                options.DefaultChallengeScheme = "Composite";
                options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
            })
            .AddScheme<AuthenticationSchemeOptions, CompositeAuthenticationHandler>("Composite", _ => { });
        services.AddClearCaseAuthorization();
        // Keys must outlive the process (and be shared between replicas) or every restart signs everyone out.
        if (builder.Configuration["DataProtection:KeyPath"] is { Length: > 0 } keyPath)
            services.AddDataProtection().SetApplicationName("clearcase")
                .PersistKeysToFileSystem(new DirectoryInfo(keyPath));
        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "clearcase.csrf";
            options.Cookie.HttpOnly = false;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = cookieSecurePolicy;
            options.HeaderName = "X-CSRF-TOKEN";
        });
        services.AddClearCaseRateLimiting(builder.Configuration);
        return builder;
    }

    private static void AddClearCaseRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var requestsPerMinute = configuration.GetValue<int?>("RateLimiting:RequestsPerMinute") ?? 600;
        if (requestsPerMinute < 1)
            throw new InvalidOperationException("RateLimiting:RequestsPerMinute must be a positive integer.");

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                return ValueTask.CompletedTask;
            };
            // Authenticated callers are limited per user, anonymous callers per address; probes are exempt.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (context.Request.Path.StartsWithSegments("/health"))
                    return RateLimitPartition.GetNoLimiter("health");
                var key = context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } user
                    ? "user:" + user
                    : "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = requestsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
            });
            options.AddPolicy("auth-login", context =>
            {
                var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
            });
        });
    }
}
