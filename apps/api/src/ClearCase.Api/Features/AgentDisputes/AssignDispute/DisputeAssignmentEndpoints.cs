using System.Security.Claims;
using ClearCase.Api.Authentication;
using ClearCase.Api.Disputes;
using ClearCase.Api.Endpoints;
using ClearCase.Api.Middleware;
using ClearCase.Application.AgentDisputes;
using ClearCase.Infrastructure.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClearCase.Api.Features.AgentDisputes.AssignDispute;

/// <summary>Staff workload slice: claiming or releasing a case never changes its processing or version.</summary>
public static class DisputeAssignmentEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/agent/disputes/{id}/assignment", Claim)
            .WithName("ClaimAgentDispute")
            .WithSummary("Claim a dispute for the signed-in employee")
            .WithDescription("Workload information only; it never blocks or changes case processing and does not bump "
                + "the case version. Claiming a case already assigned to the caller is a no-op (200). A case assigned "
                + "to another employee returns 409 dispute.alreadyAssigned. Every claim is audited append-only.")
            .Produces<AgentDisputeDetailResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AuthenticationPolicies.BankEmployeeAccess)
            .RequireCsrfProtection();

        app.MapDelete("/api/v1/agent/disputes/{id}/assignment", Release)
            .WithName("ReleaseAgentDispute")
            .WithSummary("Release a dispute assignment")
            .WithDescription("Only the assignee or a Supervisor may release; others receive 403 "
                + "dispute.assignmentForbidden. Releasing an unassigned case is a no-op (200). Every release is audited.")
            .Produces<AgentDisputeDetailResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthenticationPolicies.BankEmployeeAccess)
            .RequireCsrfProtection();
    }

    private static Task<Results<Ok<AgentDisputeDetailResponse>, ProblemHttpResult>> Claim(
        string id, HttpContext context, IDisputeAssignmentHandler handler, IWebHostEnvironment environment,
        CancellationToken cancellationToken) =>
        ExecuteAsync(id, context, environment, (disputeId, employeeId, correlationId) =>
            handler.ClaimAsync(disputeId, employeeId, correlationId, cancellationToken));

    private static Task<Results<Ok<AgentDisputeDetailResponse>, ProblemHttpResult>> Release(
        string id, HttpContext context, IDisputeAssignmentHandler handler, IWebHostEnvironment environment,
        CancellationToken cancellationToken) =>
        ExecuteAsync(id, context, environment, (disputeId, employeeId, correlationId) =>
            handler.ReleaseAsync(disputeId, employeeId, context.User.IsInRole(SyntheticIdentitySeeder.SupervisorRole),
                correlationId, cancellationToken));

    private static async Task<Results<Ok<AgentDisputeDetailResponse>, ProblemHttpResult>> ExecuteAsync(
        string id, HttpContext context, IWebHostEnvironment environment,
        Func<Guid, Guid, string, Task<DisputeAssignmentResult>> action)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(id, out var disputeId))
            return ApiProblem.Create(context, StatusCodes.Status400BadRequest, "Invalid dispute ID",
                "dispute.invalidId", errors: [new("/id", "disputeId.invalid", "Enter a valid dispute ID.")]);
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId)
            || employeeId == Guid.Empty)
            return ApiProblem.Create(context, StatusCodes.Status403Forbidden,
                "Bank employee identity is unavailable", "auth.employeeIdentityRequired");

        var result = await action(disputeId, employeeId,
            (string)context.Items[CorrelationIdMiddleware.ContextItemKey]!);
        return result.Outcome switch
        {
            DisputeAssignmentOutcome.NotFound => ApiProblem.Create(context, StatusCodes.Status404NotFound,
                "Dispute not found", "dispute.notFound"),
            DisputeAssignmentOutcome.AlreadyAssignedToOther => ApiProblem.Create(context,
                StatusCodes.Status409Conflict, "Dispute already assigned", "dispute.alreadyAssigned",
                "This dispute is assigned to another employee."),
            DisputeAssignmentOutcome.Forbidden => ApiProblem.Create(context, StatusCodes.Status403Forbidden,
                "Assignment cannot be released", "dispute.assignmentForbidden",
                "Only the assignee or a Supervisor can release this assignment."),
            _ => TypedResults.Ok(AgentDisputeDetailResponse.From(
                result.Dispute ?? throw new InvalidOperationException("Assignment result lacks the case."),
                SimulationAuthorization.IsEnabled(environment)))
        };
    }
}
