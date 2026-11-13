using ClearCase.Api.Authentication;
using ClearCase.Api.Disputes;
using ClearCase.Api.Middleware;
using ClearCase.Api.Transactions;
using ClearCase.Application.Chargebacks;
using ClearCase.Domain.Chargebacks;
using ClearCase.Infrastructure.Chargebacks;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClearCase.Api.Endpoints;

/// <summary>A local card workflow command. Evidence contains synthetic metadata, never uploaded content.</summary>
public sealed record ChargebackSimulationRequest(string? Command, RepresentmentEvidenceRequest? Evidence);

/// <summary>Identifies a synthetic receipt, delivery confirmation or authorization record.</summary>
public sealed record RepresentmentEvidenceRequest(string? Id, string? Kind);

public static class ChargebackSimulationEndpoints
{
    public static void AddChargebackSimulation(this WebApplicationBuilder builder)
    {
        if (!SimulationAuthorization.IsEnabled(builder.Environment)) return;
        builder.Services.AddScoped<IChargebackProcessingHandler, ChargebackProcessingHandler>();
        builder.Services.AddSingleton<ICardNetworkGateway>(new MockCardNetworkGateway());
        SimulationAuthorization.AddPolicy(builder, "CardNetworkSimulation", "Simulation:CardNetworkKey");
    }

    public static void MapChargebackSimulation(this WebApplication app)
    {
        if (!SimulationAuthorization.IsEnabled(app.Environment)) return;
        app.MapPost("/api/v1/disputes/{id}/chargeback-simulations", Process)
            .RequireAuthorization("CardNetworkSimulation")
            .RequireAuthorization(AuthenticationPolicies.CustomerAccess)
            .RequireCsrfProtection()
            .WithName("SimulateChargebackProcessing")
            .WithSummary("Simulate one card chargeback command (Development/Testing only)")
            .WithDescription("Requires the authenticated owner and server-configured X-Simulation-Key. "
                + "Commands: Initiate, ForwardToAcquirer, MerchantAccept, MerchantContest, ReceiveRepresentment, "
                + "ReviewRepresentment, Approve, Reject, RequestSettlement, ExecuteSettlement, Close. "
                + "ReceiveRepresentment requires evidence {id: nonempty GUID, kind: Receipt|DeliveryConfirmation|AuthorizationRecord}. "
                + "Evidence is forbidden on other commands. Approve favours the customer; only ExecuteSettlement can confirm "
                + "simulated funds. Failed/unknown operations cannot be repeated; no real money moves.")
            .Produces<DisputeResponse>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403)
            .ProducesProblem(404).ProducesProblem(409);
    }

    private static async Task<Results<Ok<DisputeResponse>, ProblemHttpResult>> Process(
        string id, ChargebackSimulationRequest request, IChargebackProcessingHandler handler,
        HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(id, out var disputeId))
            return DisputeEndpoints.Problem(context, 400, "Invalid dispute ID", "dispute.invalidId",
                errors: [new("/id", "disputeId.invalid", "Enter a valid dispute ID.")]);
        if (!Enum.TryParse<ChargebackCommand>(request.Command, out var command)
            || !Enum.IsDefined(command) || request.Command != command.ToString())
            return DisputeEndpoints.Problem(context, 400, "Invalid chargeback command", "chargeback.invalidRequest",
                errors: [new("/command", "command.invalid", "Select a supported simulation command.")]);
        var errors = new List<QueryError>();
        Guid? evidenceId = null;
        ChargebackEvidenceKind? evidenceKind = null;
        if (command == ChargebackCommand.ReceiveRepresentment)
        {
            if (!Guid.TryParse(request.Evidence?.Id, out var parsedId) || parsedId == Guid.Empty)
                errors.Add(new("/evidence/id", "evidence.idInvalid", "Provide a nonempty evidence GUID."));
            else evidenceId = parsedId;
            if (!Enum.TryParse<ChargebackEvidenceKind>(request.Evidence?.Kind, out var parsedKind)
                || !Enum.IsDefined(parsedKind) || request.Evidence?.Kind != parsedKind.ToString())
                errors.Add(new("/evidence/kind", "evidence.kindInvalid", "Select a supported synthetic evidence kind."));
            else evidenceKind = parsedKind;
        }
        else if (request.Evidence is not null)
            errors.Add(new("/evidence", "evidence.notAllowed", "Evidence is only accepted with ReceiveRepresentment."));
        if (errors.Count != 0)
            return DisputeEndpoints.Problem(context, 400, "Invalid representment metadata", "chargeback.invalidRequest", errors: errors);
        try
        {
            var dispute = await handler.HandleAsync(new ChargebackProcessingCommand(
                disputeId, command, evidenceId, evidenceKind,
                (string)context.Items[CorrelationIdMiddleware.ContextItemKey]!), cancellationToken);
            return dispute is null
                ? DisputeEndpoints.Problem(context, 404, "Dispute not found", "dispute.notFound")
                : TypedResults.Ok(DisputeResponse.From(dispute));
        }
        catch (InvalidChargebackTransitionException)
        {
            return DisputeEndpoints.Problem(context, 409, "Invalid chargeback transition",
                "chargeback.invalidTransition", "This action is not allowed or requires further review.");
        }
    }
}
