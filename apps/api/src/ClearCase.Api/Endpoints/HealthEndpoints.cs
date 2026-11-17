using Microsoft.AspNetCore.Http.HttpResults;
using ClearCase.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Api.Endpoints;

public static class HealthEndpoints
{
    public static WebApplication MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health/live", () => TypedResults.Ok(new HealthResponse(
                "live",
                new Dictionary<string, string> { ["application"] = "live" })))
            .WithName("HealthLive")
            .WithSummary("Check whether the API process is responsive")
            .WithDescription("Reports process liveness without exposing system details.")
            .Produces<HealthResponse>(StatusCodes.Status200OK);

        app.MapGet("/health/ready", GetReadiness)
            .WithName("HealthReady")
            .WithSummary("Check application startup, database connectivity and applied schema")
            .WithDescription("Readiness requires application startup, SQLite connectivity and no pending EF migrations.")
            .Produces<HealthResponse>(StatusCodes.Status200OK)
            .Produces<HealthResponse>(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> GetReadiness(
        IHostApplicationLifetime applicationLifetime,
        TransactionDbContext database,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!applicationLifetime.ApplicationStarted.IsCancellationRequested)
        {
            return TypedResults.Json(new HealthResponse(
                "not_ready", new Dictionary<string, string>
                {
                    ["application"] = "starting",
                    ["database"] = "not_checked"
                }), statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            if (await database.Database.CanConnectAsync(cancellationToken)
                && !(await database.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
                return TypedResults.Ok(new HealthResponse("ready", new Dictionary<string, string>
                {
                    ["application"] = "ready",
                    ["database"] = "ready"
                }));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            loggerFactory.CreateLogger("ClearCase.Readiness")
                .LogWarning(error, "Readiness dependency check failed.");
        }

        return TypedResults.Json(new HealthResponse(
            "not_ready", new Dictionary<string, string>
            {
                ["application"] = "ready",
                ["database"] = "unavailable"
            }), statusCode: StatusCodes.Status503ServiceUnavailable);
    }

}

/// <summary>Provides a non-sensitive machine-readable health status.</summary>
public sealed record HealthResponse(string Status, IReadOnlyDictionary<string, string> Checks);