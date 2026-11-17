using System.Security.Claims;
using ClearCase.Api.Authentication;
using ClearCase.Application.Disputes;
using ClearCase.Infrastructure.Disputes;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClearCase.Api.Endpoints;

/// <summary>No outbox payload or private audit details are returned by this operational contract.</summary>
public sealed record RequeueOutboxRequest(string? Reason);
public sealed record RequeuedOutboxResponse(Guid EventId, string Status);

public static class OutboxRecoveryEndpoints
{
    public static void MapOutboxRecoveryEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/supervisor/outbox/parked", List)
            .RequireAuthorization(AuthenticationPolicies.SupervisorAccess)
            .WithName("ListParkedOutboxMessages")
            .WithDescription("Supervisor-only safe parked-message metadata. Never exposes raw event payloads.")
            .Produces<ParkedOutboxItem[]>(StatusCodes.Status200OK)
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(400);
        app.MapPost("/api/v1/supervisor/outbox/{id}/requeue", Requeue)
            .RequireAuthorization(AuthenticationPolicies.SupervisorAccess)
            .RequireCsrfProtection()
            .WithName("RequeueParkedOutboxMessage")
            .WithDescription("Audited Supervisor requeue after verifying external broker/consumer effects. Requires reason and CSRF.")
            .Produces<RequeuedOutboxResponse>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403)
            .ProducesProblem(404).ProducesProblem(409);
    }

    private static async Task<Results<Ok<IReadOnlyList<ParkedOutboxItem>>, ProblemHttpResult>> List(
        int? limit, HttpContext context, IOutboxRecoveryStore store,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (limit is < 1 or > 100)
            return ApiProblem.Create(context, 400, "Invalid outbox query",
                "outbox.invalidQuery", "Choose a limit from 1 to 100.");
        var records = await store.ListParkedAsync(limit ?? 50, cancellationToken);
        return TypedResults.Ok(records);
    }

    private static async Task<Results<Ok<RequeuedOutboxResponse>, ProblemHttpResult>> Requeue(
        string id, RequeueOutboxRequest request, HttpContext context,
        IOutboxRecoveryStore store, TimeProvider clock, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(id, out var eventId) || eventId == Guid.Empty)
            return ApiProblem.Create(context, 400, "Invalid event ID", "outbox.invalidId");
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId)
            || employeeId == Guid.Empty)
            return ApiProblem.Create(context, 403, "Supervisor identity required", "auth.employeeIdentityRequired");

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 15 or > 500)
            return ApiProblem.Create(context, 400, "Recovery reason required",
                "outbox.invalidReason", "Record a reason of 15–500 characters after checking broker delivery.");

        var outcome = await store.RequeueAsync(eventId, employeeId, reason,
            clock.GetUtcNow(), cancellationToken);
        if (outcome == OutboxRequeueOutcome.NotFound)
            return ApiProblem.Create(context, 404, "Outbox event not found",
                "outbox.notFound");
        if (outcome == OutboxRequeueOutcome.NotParked)
            return ApiProblem.Create(context, 409, "Outbox event is not parked",
                "outbox.notParked", "Refresh parked events. Only parked, unpublished messages can be requeued.");

        OutboxDispatchMetrics.Requeued.Add(1);
        return TypedResults.Ok(new RequeuedOutboxResponse(eventId, "Requeued"));
    }
}
