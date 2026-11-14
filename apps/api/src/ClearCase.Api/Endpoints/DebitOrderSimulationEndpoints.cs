using ClearCase.Api.Authentication;
using ClearCase.Api.Disputes;
using ClearCase.Api.Middleware;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Disputes;
using ClearCase.Infrastructure.Disputes;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClearCase.Api.Endpoints;

/// <summary>A privileged local simulation command, never a customer processing request.</summary>
public sealed record DebitOrderSimulationRequest(string? Command, long? ExpectedVersion = null);

public static class DebitOrderSimulationEndpoints
{
    public static void AddDebitOrderSimulation(this WebApplicationBuilder builder)
    {
        if (!SimulationAuthorization.IsEnabled(builder.Environment)) return;
        builder.Services.AddScoped<IDebitOrderProcessingHandler, DebitOrderProcessingHandler>();
        var outcome = ReadOutcome(builder.Configuration, "Simulation:DebitOrderOutcome",
            DebitOrderGatewayOutcome.Confirmed);
        var reconciliationOutcome = ReadOutcome(builder.Configuration, "Simulation:DebitOrderStatus",
            DebitOrderGatewayOutcome.Unknown);
        builder.Services.AddSingleton<IDebitOrderProcessingGateway>(provider =>
            new ResilientDebitOrderProcessingGateway(new MockDebitOrderProcessingGateway(outcome, reconciliationOutcome),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<ResilientDebitOrderProcessingGateway>>()));
        SimulationAuthorization.AddPolicy(builder, "DebitOrderSimulation", "Simulation:DebitOrderKey");
    }

    private static DebitOrderGatewayOutcome ReadOutcome(
        IConfiguration configuration, string key, DebitOrderGatewayOutcome defaultValue)
    {
        var value = configuration[key];
        if (value is null)
            return defaultValue;
        if (Enum.TryParse<DebitOrderGatewayOutcome>(value, out var outcome) && Enum.IsDefined(outcome))
            return outcome;
        throw new InvalidOperationException($"{key} must be a supported debit-order gateway outcome.");
    }

    public static void MapDebitOrderSimulation(this WebApplication app)
    {
        if (!SimulationAuthorization.IsEnabled(app.Environment)) return;
        app.MapPost("/api/v1/disputes/{id}/debit-order-simulations", Process)
            .RequireAuthorization("DebitOrderSimulation")
            .RequireAuthorization(AuthenticationPolicies.CustomerAccess)
            .RequireCsrfProtection()
            .WithName("SimulateDebitOrderProcessing")
            .WithSummary("Simulate one debit-order processing command (local demo only)")
            .WithDescription("Requires authentication and the server-configured X-Simulation-Key. "
                + "Commands: StartReview, VerifyMandate, CompleteInvestigation, RequestReversal, ExecuteReversal, "
                + "ReconcileReversal, Reject, Close. ReconcileReversal queries the original stable operation reference "
                + "and never submits another reversal. No real money moves. Include the current expectedVersion "
                + "from the dispute representation to reject stale updates.")
            .Produces<DisputeResponse>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403)
            .ProducesProblem(404).ProducesProblem(409);
    }

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
        if (request.ExpectedVersion is <= 0)
            return DisputeEndpoints.Problem(context, 400, "Invalid expected version", "dispute.invalidRequest",
                errors: [new("/expectedVersion", "expectedVersion.invalid", "Expected version must be a positive integer.")]);
        try
        {
            var dispute = await handler.HandleAsync(disputeId, command, cancellationToken, request.ExpectedVersion,
                (string)context.Items[CorrelationIdMiddleware.ContextItemKey]!);
            return dispute is null
                ? DisputeEndpoints.Problem(context, 404, "Dispute not found", "dispute.notFound")
                : TypedResults.Ok(DisputeResponse.From(dispute));
        }
        catch (InvalidDebitOrderTransitionException)
        {
            return DisputeEndpoints.Problem(context, 409, "Invalid debit-order transition",
                "debitOrder.invalidTransition", "This action is not allowed or requires further mandate and policy investigation.");
        }
        catch (DisputeConcurrencyConflictException)
        {
            return DisputeEndpoints.Problem(context, 409, "Dispute version conflict", "dispute.concurrencyConflict",
                "The dispute changed since it was read. Refresh the case before trying again.");
        }
    }
}
