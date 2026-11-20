using System.Globalization;
using ClearCase.Api.Authentication;
using ClearCase.Api.Endpoints;
using ClearCase.Api.Transactions;
using ClearCase.Application.Notifications;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace ClearCase.Api.Features.Notifications;

public sealed record NotificationResponse(
    Guid Id, Guid DisputeId, string DisputeReference, string EventType, string Message,
    DateTimeOffset OccurredAt, bool IsRead)
{
    public static NotificationResponse From(CustomerNotification item) =>
        new(item.Id, item.DisputeId, item.DisputeReference, item.EventType, item.Message, item.OccurredAt, item.IsRead);
}

public sealed record NotificationFeedResponse(int UnreadCount, IReadOnlyList<NotificationResponse> Items);

public static class NotificationEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/notifications", List)
            .WithName("ListNotifications")
            .WithSummary("In-app notifications derived from the customer's dispute timelines")
            .WithDescription("Newest first; limit is 1 to 50 (default 20). Messages are the customer-safe public timeline "
                + "descriptions and never include staff notes. isRead is true when the event is not later than the "
                + "customer's read marker. This is an in-app feed only: no email, SMS or push is sent. Automatic polls "
                + "should send X-Background-Check: true so they do not extend the signed-in session.")
            .Produces<NotificationFeedResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(AuthenticationPolicies.CustomerAccess);

        app.MapPost("/api/v1/notifications/read", MarkRead)
            .WithName("MarkNotificationsRead")
            .WithSummary("Mark all current notifications as read")
            .WithDescription("Moves the caller's read marker to the current time. Safe to repeat.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(AuthenticationPolicies.CustomerAccess)
            .RequireCsrfProtection();
    }

    private static async Task<Results<Ok<NotificationFeedResponse>, ProblemHttpResult>> List(
        [FromQuery] string? limit, HttpContext context, IGetNotificationsHandler handler,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var size = GetNotificationsHandler.DefaultLimit;
        if (limit is not null && (!int.TryParse(limit, NumberStyles.None, CultureInfo.InvariantCulture, out size)
            || size is < 1 or > GetNotificationsHandler.MaximumLimit))
            return ApiProblem.Create(context, StatusCodes.Status400BadRequest, "Invalid notification query",
                "notification.invalidQuery",
                errors: [new("/limit", "limit.invalid",
                    $"Choose a limit from 1 to {GetNotificationsHandler.MaximumLimit}.")]);

        var feed = await handler.HandleAsync(size, cancellationToken);
        return TypedResults.Ok(new NotificationFeedResponse(
            feed.UnreadCount, feed.Items.Select(NotificationResponse.From).ToArray()));
    }

    private static async Task<NoContent> MarkRead(
        HttpContext context, IMarkNotificationsReadHandler handler, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        await handler.HandleAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}
