using ClearCase.Api.Endpoints;
using ClearCase.Api.Transactions;
using ClearCase.Application.AgentDisputes;
using ClearCase.Application.Disputes;
using ClearCase.Domain.Chargebacks;
using ClearCase.Domain.Disputes;
using Microsoft.AspNetCore.Diagnostics;

namespace ClearCase.Api.Middleware;

public sealed class DomainExceptionHandler(ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    private const string AgentRoutePrefix = "/api/v1/agent/";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var problem = Map(context, exception);
        if (problem is null)
            return false;

        if (exception is DisputeProcessingUnavailableException)
            logger.LogWarning("A command could not execute because a processing adapter is unavailable.");
        context.Response.Headers.CacheControl = "private, no-store";
        await problem.ExecuteAsync(context);
        return true;
    }

    private static IResult? Map(HttpContext context, Exception exception) => exception switch
    {
        DisputeConcurrencyConflictException => ApiProblem.Create(context, 409, "Dispute version conflict",
            "dispute.concurrencyConflict",
            "The dispute changed since it was read. Refresh the case before trying again."),
        ActiveDisputeEscalationException => ApiProblem.Create(context, 409, "Supervisor review is pending",
            "dispute.activeEscalation", "Resolve the active escalation before continuing case processing."),
        DisputeWithdrawalNotAllowedException => ApiProblem.Create(context, 409, "Dispute cannot be withdrawn",
            "dispute.withdrawalNotAllowed",
            "This dispute can no longer be withdrawn because it has been decided, escalated or a payment step has started."),
        InvalidDebitOrderTransitionException => InvalidTransition(context, "debitOrder", "Invalid debit-order transition",
            "This action is not allowed or requires further mandate and policy investigation."),
        InvalidBankChargeTransitionException => InvalidTransition(context, "bankCharge", "Invalid bank-charge transition",
            "This action is not allowed or requires further investigation."),
        InvalidChargebackTransitionException => InvalidTransition(context, "chargeback", "Invalid chargeback transition",
            "This action is not allowed or requires further review."),
        InvalidAgentDisputeCommandException => ApiProblem.Create(context, 400, "Invalid staff command",
            "agentDispute.invalidRequest",
            errors: [new("/command", "command.invalid", "Select a supported command for this case category.")]),
        DisputeProcessingUnavailableException => ApiProblem.Create(context, 503, "Processing unavailable",
            "dispute.processingUnavailable",
            "The required processing adapter is not configured. No transition was committed."),
        _ => null
    };

    // Staff routes expose one transition code across categories; customer simulation routes keep per-category codes.
    private static IResult InvalidTransition(HttpContext context, string category, string title, string detail) =>
        context.Request.Path.StartsWithSegments(AgentRoutePrefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            ? ApiProblem.Create(context, 409, "Invalid dispute transition", "dispute.invalidTransition",
                "This action is not allowed or requires further investigation.")
            : ApiProblem.Create(context, 409, title, category + ".invalidTransition", detail);
}
