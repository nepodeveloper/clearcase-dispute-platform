using ClearCase.Api.Endpoints;
using ClearCase.Api.Middleware;
using ClearCase.Api.Transactions;
using ClearCase.Api.Authentication;
using ClearCase.Application.Transactions;
using ClearCase.Application.Disputes;
using ClearCase.Application.DisputeEligibility;
using ClearCase.Domain.DisputeEligibility;
using ClearCase.Domain.Transactions;
using ClearCase.Infrastructure.Authentication;
using ClearCase.Infrastructure.Transactions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
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
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "clearcase.auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing")
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
});
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "Composite";
        options.DefaultChallengeScheme = "Composite";
        options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
    })
    .AddScheme<AuthenticationSchemeOptions, CompositeAuthenticationHandler>("Composite", _ => { });
builder.Services.AddClearCaseAuthorization();
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "clearcase.csrf";
    options.Cookie.HttpOnly = false;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing")
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.HeaderName = "X-CSRF-TOKEN";
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
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
if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddSingleton<IDebitOrderMandateReader, MockDebitOrderMandateReader>();
}
else
{
    builder.Services.AddSingleton<IDebitOrderMandateReader, UnavailableDebitOrderMandateReader>();
}
builder.AddDebitOrderSimulation();
builder.AddChargebackSimulation();
builder.AddBankChargeSimulation();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICustomerContext, HttpCustomerContext>();
builder.Services.AddScoped<IListTransactionsHandler, ListTransactionsHandler>();
builder.Services.AddScoped<IGetTransactionDetailsHandler, GetTransactionDetailsHandler>();
builder.Services.AddScoped<IGetDisputeEligibilityHandler, GetDisputeEligibilityHandler>();
builder.Services.AddScoped<DisputeEligibilityEvaluator>();
builder.Services.AddScoped<ISubmitDisputeHandler, SubmitDisputeHandler>();
builder.Services.AddScoped<IGetDisputeHandler, GetDisputeHandler>();
builder.Services.AddScoped<IListDisputesHandler, ListDisputesHandler>();
builder.Services.AddTransactionPersistence(
    builder.Environment.ContentRootPath, builder.Environment.WebRootPath,
    builder.Environment.IsDevelopment());
var eligibilitySettings = builder.Configuration.GetSection("DisputeEligibility")
    .Get<DisputeEligibilityPolicySettings>()
    ?? throw new InvalidOperationException("DisputeEligibility configuration is required.");
builder.Services.AddSingleton(eligibilitySettings);
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<IDisputeEligibilityPolicy, CardPurchaseEligibilityPolicy>();
builder.Services.AddSingleton<IDisputeEligibilityPolicy, DebitOrderEligibilityPolicy>();
builder.Services.AddSingleton<IDisputeEligibilityPolicy, BankChargeEligibilityPolicy>();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        if (context.HttpContext.Items.TryGetValue(
                CorrelationIdMiddleware.ContextItemKey,
                out var correlationId))
        {
            context.ProblemDetails.Extensions["correlationId"] = correlationId;
        }
    };
});

if (builder.Environment.IsDevelopment())
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["http://localhost:4200"];

    builder.Services.AddCors(options => options.AddPolicy(
        "LocalAngularDevelopment",
        policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.Logger.LogInformation("Applying SQLite migrations and synthetic development seed.");
    using var scope = app.Services.CreateScope();
    var database = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
    await database.Database.MigrateAsync();
    await SyntheticTransactionSeeder.SeedAsync(database, CancellationToken.None);
    await SyntheticIdentitySeeder.SeedAsync(database,
        builder.Configuration.GetSection("Authentication:SeedPasswords").Get<Dictionary<string, string?>>() ?? [],
        CancellationToken.None);
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseCors("LocalAngularDevelopment");
    app.MapOpenApi();
}

app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapTransactionEndpoints();
app.MapDisputeEndpoints();
app.MapDebitOrderSimulation();
app.MapChargebackSimulation();
app.MapBankChargeSimulation();

app.Run();

public partial class Program { }
