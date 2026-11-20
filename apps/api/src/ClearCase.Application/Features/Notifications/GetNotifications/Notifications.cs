using ClearCase.Application.Transactions;

namespace ClearCase.Application.Notifications;

public sealed record CustomerNotification(
    Guid Id, Guid DisputeId, string DisputeReference, string EventType, string Message,
    DateTimeOffset OccurredAt, bool IsRead);

public sealed record CustomerNotificationFeed(int UnreadCount, IReadOnlyList<CustomerNotification> Items);

public interface ICustomerNotificationStore
{
    Task<CustomerNotificationFeed> ReadAsync(Guid customerId, int limit, CancellationToken cancellationToken);

    Task MarkReadAsync(Guid customerId, DateTimeOffset readThrough, CancellationToken cancellationToken);
}

public interface IGetNotificationsHandler
{
    Task<CustomerNotificationFeed> HandleAsync(int limit, CancellationToken cancellationToken);
}

public interface IMarkNotificationsReadHandler
{
    Task HandleAsync(CancellationToken cancellationToken);
}

public sealed class GetNotificationsHandler(ICustomerContext customer, ICustomerNotificationStore store)
    : IGetNotificationsHandler
{
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 50;

    public Task<CustomerNotificationFeed> HandleAsync(int limit, CancellationToken cancellationToken) =>
        store.ReadAsync(customer.CustomerId, limit, cancellationToken);
}

public sealed class MarkNotificationsReadHandler(
    ICustomerContext customer, ICustomerNotificationStore store, TimeProvider clock)
    : IMarkNotificationsReadHandler
{
    public Task HandleAsync(CancellationToken cancellationToken) =>
        store.MarkReadAsync(customer.CustomerId, clock.GetUtcNow(), cancellationToken);
}
