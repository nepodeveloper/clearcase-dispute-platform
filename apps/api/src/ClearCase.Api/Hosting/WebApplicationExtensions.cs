using ClearCase.Api.Endpoints;
using ClearCase.Api.Features;
using ClearCase.Api.Middleware;
using ClearCase.Infrastructure.Authentication;
using ClearCase.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Api.Hosting;

public static class WebApplicationExtensions
{
    public const string MigrateOnlyArgument = "--migrate-only";

    /// <summary>
    /// Development always migrates and seeds synthetic data. Other environments migrate only when
    /// Persistence:MigrateOnStartup is true or the process is run with --migrate-only (for a deployment job).
    /// </summary>
    public static async Task PrepareDatabaseAsync(this WebApplication app, string[] args)
    {
        var migrateOnly = args.Contains(MigrateOnlyArgument, StringComparer.Ordinal);
        var development = app.Environment.IsDevelopment();
        if (!development && !migrateOnly && !app.Configuration.GetValue<bool>("Persistence:MigrateOnStartup"))
            return;

        app.Logger.LogInformation("Applying database migrations.");
        using var scope = app.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        await database.Database.MigrateAsync();
        if (!development)
            return;

        app.Logger.LogInformation("Applying synthetic development seed.");
        await SyntheticTransactionSeeder.SeedAsync(database, CancellationToken.None,
            scope.ServiceProvider.GetRequiredService<TimeProvider>());
        await SyntheticIdentitySeeder.SeedAsync(database,
            app.Configuration.GetSection("Authentication:SeedPasswords").Get<Dictionary<string, string?>>() ?? [],
            CancellationToken.None);
    }

    public static WebApplication UseClearCasePipeline(this WebApplication app)
    {
        if (app.Configuration.HasTrustedProxies())
            app.UseForwardedHeaders();
        // Security headers also apply to redirects, which short-circuit downstream middleware.
        app.UseMiddleware<ApiSecurityHeadersMiddleware>();
        if (app.Environment.IsProduction())
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseAuthentication();
        // After authentication so the global limiter can partition authenticated callers by user.
        app.UseRateLimiter();
        app.UseAuthorization();

        if (app.Environment.IsDevelopment())
        {
            app.UseCors("LocalAngularDevelopment");
            app.MapOpenApi();
        }
        return app;
    }

    public static WebApplication MapClearCaseEndpoints(this WebApplication app)
    {
        app.MapHealthEndpoints();
        app.MapAuthEndpoints();
        app.MapCustomerFeatures();
        app.MapAgentDisputeEndpoints();
        app.MapAgentDisputeCommands();
        app.MapSupervisorEscalationEndpoints();
        app.MapOutboxRecoveryEndpoints();
        app.MapDebitOrderSimulation();
        app.MapChargebackSimulation();
        app.MapBankChargeSimulation();
        return app;
    }
}
