using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ClearCase.Api.Authentication;
using ClearCase.Api.Disputes;
using ClearCase.Api.Middleware;
using ClearCase.Api.Transactions;
using ClearCase.Application.AgentDisputes;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Chargebacks;
using ClearCase.Domain.Disputes;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClearCase.Api.Endpoints;

/// <summary>A staff workflow command against a known case version. Identity is supplied only by the session.</summary>
public sealed record AgentDisputeCommandRequest(
    [property: Required] string? Command,
    [property: Required] long? ExpectedVersion,
    RepresentmentEvidenceRequest? Evidence = null);

public static class AgentDisputeCommandEndpoints
{
    public static void MapAgentDisputeCommands(this WebApplication app) =>
        app.MapPost("/api/v1/agent/disputes/{id}/commands", Process)
            .RequireAuthorization(AuthenticationPolicies.BankEmployeeAccess)
            .RequireCsrfProtection()
            .WithName("ProcessAgentDisputeCommand")
            .WithSummary("Apply a version-guarded staff dispute command")
            .WithDescription("Agent or Supervisor, in the existing bank-wide scope. Requires expectedVersion and CSRF, "
                + "not a simulation key. Commands are category-specific and retain existing workflow guards. "
                + "Employee identity is audited atomically; it is never taken from JSON. Repeating a committed request "
                + "returns 409 rather than executing again. Financial adapters are simulated only in Development/Testing; "
                + "unconfigured adapters return 503 without changing the case.")
            .Produces<DisputeResponse>().ProducesProblem(400).ProducesProblem(401)
            .ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);

    private static async Task<Results<Ok<DisputeResponse>, ProblemHttpResult>> Process(
        string id, AgentDisputeCommandRequest request, IAgentDisputeCommandHandler handler,
        HttpContext context, IWebHostEnvironment environment, ILogger<AgentDisputeCommandHandler> logger,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId)
            || employeeId == Guid.Empty)
            return ApiProblem.Create(context, 403, "Bank employee identity is unavailable", "auth.employeeIdentityRequired");
        if (!Guid.TryParse(id, out var disputeId))
            return ApiProblem.Create(context, 400, "Invalid dispute ID", "dispute.invalidId",
                errors: [new("/id", "disputeId.invalid", "Enter a valid dispute ID.")]);

        if (request.ExpectedVersion is not { } expectedVersion || expectedVersion <= 0)
            return ApiProblem.Create(context, 400, "Invalid staff command", "agentDispute.invalidRequest",
                errors: [new("/expectedVersion", "expectedVersion.invalid", "Send the current positive case version.")]);
        var errors = new List<QueryError>();
        RepresentmentEvidenceValidation.Validate(request.Command, request.Evidence, errors,
            out var evidenceId, out var evidenceKind);
        if (errors.Count != 0)
            return ApiProblem.Create(context, 400, "Invalid staff command", "agentDispute.invalidRequest", errors: errors);
        try
        {
            var dispute = await handler.HandleAsync(new(disputeId, employeeId, request.Command,
                expectedVersion, evidenceId, evidenceKind,
                (string)context.Items[CorrelationIdMiddleware.ContextItemKey]!,
                SimulationAuthorization.IsEnabled(environment)), cancellationToken);
            return dispute is null
                ? ApiProblem.Create(context, 404, "Dispute not found", "dispute.notFound")
                : TypedResults.Ok(DisputeResponse.From(dispute));
        }
        catch (InvalidAgentDisputeCommandException)
        {
            return ApiProblem.Create(context, 400, "Invalid staff command", "agentDispute.invalidRequest",
                errors: [new("/command", "command.invalid", "Select a supported command for this case category.")]);
        }
        catch (DisputeConcurrencyConflictException)
        {
            return ApiProblem.Create(context, 409, "Dispute version conflict", "dispute.concurrencyConflict",
                "The dispute changed since it was read. Refresh the case before trying again.");
        }
        catch (ActiveDisputeEscalationException)
        {
            return ApiProblem.Create(context, 409, "Supervisor review is pending", "dispute.activeEscalation",
                "Resolve the active escalation before continuing case processing.");
        }
        catch (Exception exception) when (exception is InvalidDebitOrderTransitionException
            or InvalidBankChargeTransitionException or InvalidChargebackTransitionException)
        {
            return ApiProblem.Create(context, 409, "Invalid dispute transition", "dispute.invalidTransition",
                "This action is not allowed or requires further investigation.");
        }
        catch (DisputeProcessingUnavailableException)
        {
            logger.LogWarning("Staff command could not execute because a processing adapter is unavailable.");
            return ApiProblem.Create(context, 503, "Processing unavailable", "dispute.processingUnavailable",
                "The required processing adapter is not configured. No transition was committed.");
        }
    }
}
