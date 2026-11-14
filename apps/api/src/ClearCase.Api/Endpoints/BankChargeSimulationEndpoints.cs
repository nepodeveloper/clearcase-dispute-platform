using ClearCase.Api.Authentication;
using ClearCase.Api.Disputes;
using ClearCase.Api.Middleware;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Disputes;
using ClearCase.Infrastructure.Disputes;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClearCase.Api.Endpoints;

/// <summary>A Development/Testing-only internal fee review command.</summary>
public sealed record BankChargeSimulationRequest(string? Command, long? ExpectedVersion = null);

public static class BankChargeSimulationEndpoints
{
    public static void AddBankChargeSimulation(this WebApplicationBuilder builder)
    {
        if (!SimulationAuthorization.IsEnabled(builder.Environment)) return;
        builder.Services.AddScoped<IBankChargeProcessingHandler, BankChargeProcessingHandler>();
        builder.Services.AddSingleton<IBankChargeAdjustmentGateway>(new MockBankChargeAdjustmentGateway());
        SimulationAuthorization.AddPolicy(builder, "BankChargeSimulation", "Simulation:BankChargeKey");
    }

    public static void MapBankChargeSimulation(this WebApplication app)
    {
        if (!SimulationAuthorization.IsEnabled(app.Environment)) return;
        app.MapPost("/api/v1/disputes/{id}/bank-charge-simulations", Process)
            .RequireAuthorization("BankChargeSimulation")
            .RequireAuthorization(AuthenticationPolicies.CustomerAccess)
            .RequireCsrfProtection()
            .WithName("SimulateBankChargeProcessing")
            .WithSummary("Simulate internal fee investigation and adjustment")
            .WithDescription("Development/Testing only. Requires the authenticated owner and server-configured X-Simulation-Key. "
                + "Commands: StartReview, Approve, Reject, RequestAdjustment, ExecuteAdjustment, Close. "
                + "Only an explicit mock adjustment result confirms funds; no real money moves. Include the current expectedVersion "
                + "from the dispute representation to reject stale updates.")
            .Produces<DisputeResponse>().ProducesProblem(400).ProducesProblem(401)
            .ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
    }

    private static async Task<Results<Ok<DisputeResponse>, ProblemHttpResult>> Process(
        string id, BankChargeSimulationRequest request, IBankChargeProcessingHandler handler,
        HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(id, out var disputeId))
            return DisputeEndpoints.Problem(context, 400, "Invalid dispute ID", "dispute.invalidId",
                errors: [new("/id", "disputeId.invalid", "Enter a valid dispute ID.")]);
        if (!Enum.TryParse<BankChargeCommand>(request.Command, out var command)
            || !Enum.IsDefined(command) || request.Command != command.ToString())
            return DisputeEndpoints.Problem(context, 400, "Invalid bank-charge command", "bankCharge.invalidRequest",
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
        catch (InvalidBankChargeTransitionException)
        {
            return DisputeEndpoints.Problem(context, 409, "Invalid bank-charge transition",
                "bankCharge.invalidTransition", "This action is not allowed or requires further investigation.");
        }
        catch (DisputeConcurrencyConflictException)
        {
            return DisputeEndpoints.Problem(context, 409, "Dispute version conflict", "dispute.concurrencyConflict",
                "The dispute changed since it was read. Refresh the case before trying again.");
        }
    }
}
