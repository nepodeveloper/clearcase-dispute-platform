using System.Security.Cryptography;
using System.Text;
using ClearCase.Api.Disputes;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Disputes;
using ClearCase.Infrastructure.Disputes;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClearCase.Api.Endpoints;

/// <summary>A privileged local simulation command, never a customer processing request.</summary>
public sealed record DebitOrderSimulationRequest(string? Command);

public static class DebitOrderSimulationEndpoints
{
    public static void AddDebitOrderSimulation(this WebApplicationBuilder builder)
    {
        if (!Enabled(builder.Environment)) return;
        builder.Services.AddScoped<IDebitOrderProcessingHandler, DebitOrderProcessingHandler>();
        builder.Services.AddSingleton<IDebitOrderProcessingGateway>(new MockDebitOrderProcessingGateway());
        builder.Services.AddAuthorization(options => options.AddPolicy("DebitOrderSimulation", policy =>
            policy.RequireAuthenticatedUser().RequireAssertion(context =>
            {
                var key = builder.Configuration["Simulation:DebitOrderKey"];
                return !string.IsNullOrWhiteSpace(key) && context.Resource is HttpContext http
                    && http.Request.Headers["X-Simulation-Key"] is { Count: 1 } supplied
                    && CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(supplied.ToString()));
            })));
    }

    public static void MapDebitOrderSimulation(this WebApplication app)
    {
        if (!Enabled(app.Environment)) return;
        app.MapPost("/api/v1/disputes/{id}/debit-order-simulations", Process)
            .RequireAuthorization("DebitOrderSimulation")
            .WithName("SimulateDebitOrderProcessing")
            .WithSummary("Simulate one debit-order processing command (local demo only)")
            .WithDescription("Requires authentication and the server-configured X-Simulation-Key. "
                + "Commands: StartReview, VerifyMandate, CompleteInvestigation, RequestReversal, ExecuteReversal, Reject, Close. "
                + "No real money moves. Unknown outcomes cannot be retried.")
            .Produces<DisputeResponse>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403)
            .ProducesProblem(404).ProducesProblem(409);
    }

    private static bool Enabled(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Testing");

    private static async Task<Results<Ok<DisputeResponse>, ProblemHttpResult>> Process(
        string id, DebitOrderSimulationRequest request, IDebitOrderProcessingHandler handler,
        HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(id, out var disputeId))
            return DisputeEndpoints.Problem(context, 400, "Invalid dispute ID", "dispute.invalidId",
                errors: [new("/id", "disputeId.invalid", "Enter a valid dispute ID.")]);
        if (!Enum.TryParse<DebitOrderCommand>(request.Command, out var command)
            || !Enum.IsDefined(command) || request.Command != command.ToString())
            return DisputeEndpoints.Problem(context, 400, "Invalid simulation command", "debitOrder.invalidCommand",
                errors: [new("/command", "command.invalid", "Select a supported simulation command.")]);
        try
        {
            var dispute = await handler.HandleAsync(disputeId, command, cancellationToken);
            return dispute is null
                ? DisputeEndpoints.Problem(context, 404, "Dispute not found", "dispute.notFound")
                : TypedResults.Ok(DisputeResponse.From(dispute));
        }
        catch (InvalidDebitOrderTransitionException)
        {
            return DisputeEndpoints.Problem(context, 409, "Invalid debit-order transition",
                "debitOrder.invalidTransition", "This action is not allowed or requires further mandate and policy investigation.");
        }
    }
}
