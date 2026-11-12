using Microsoft.AspNetCore.Http.HttpResults;

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
            .WithSummary("Check whether the API has completed startup")
            .WithDescription("Readiness checks application startup only; no external dependencies are checked.")
            .Produces<HealthResponse>(StatusCodes.Status200OK)
            .Produces<HealthResponse>(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static Results<Ok<HealthResponse>, JsonHttpResult<HealthResponse>> GetReadiness(
        IHostApplicationLifetime applicationLifetime)
    {
        if (!applicationLifetime.ApplicationStarted.IsCancellationRequested)
        {
            return TypedResults.Json(
                new HealthResponse(
                    "not_ready",
                    new Dictionary<string, string> { ["application"] = "starting" }),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return TypedResults.Ok(new HealthResponse(
            "ready",
            new Dictionary<string, string> { ["application"] = "ready" }));
    }
}

/// <summary>Provides a non-sensitive machine-readable health status.</summary>
public sealed record HealthResponse(string Status, IReadOnlyDictionary<string, string> Checks);