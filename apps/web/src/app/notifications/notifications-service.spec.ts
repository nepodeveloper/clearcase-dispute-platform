import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AppNotification, NotificationsService, notificationPollMs } from './notifications-service';

describe('NotificationsService', () => {
  const unread: AppNotification = {
    id: 'n1', disputeId: 'd1', disputeReference: 'DSP-1', eventType: 'UnderReview',
    message: 'Your dispute is under review.', occurredAt: '2026-11-13T10:00:00Z', isRead: false,
  };
  let http: HttpTestingController;
  let service: NotificationsService;
  let visibility: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(NotificationsService);
  });

  afterEach(() => {
    service.stop();
    visibility.mockRestore();
    vi.useRealTimers();
    http.verify();
  });

  const poll = () => http.expectOne(request => request.url === '/api/v1/notifications');
  // Responses are consumed through promises, so let their continuations run before asserting.
  const settle = async () => { for (let tick = 0; tick < 5; tick++) await Promise.resolve(); };

  it('polls only the unread count at start, every 60 seconds, and never while the page is hidden', async () => {
    service.start();
    service.start();
    const first = poll();
    expect(first.request.params.get('limit')).toBe('1');
    first.flush({ unreadCount: 3, items: [] });
    await settle();
    expect(service.unreadCount()).toBe(3);

    vi.advanceTimersByTime(notificationPollMs - 1);
    http.expectNone(request => request.url === '/api/v1/notifications');
    vi.advanceTimersByTime(1);
    poll().flush({ unreadCount: 4, items: [] });
    await settle();
    expect(service.unreadCount()).toBe(4);
    expect(service.items()).toEqual([]);

    visibility.mockReturnValue('hidden');
    vi.advanceTimersByTime(notificationPollMs * 2);
    http.expectNone(request => request.url === '/api/v1/notifications');

    visibility.mockReturnValue('visible');
    document.dispatchEvent(new Event('visibilitychange'));
    poll().flush({ unreadCount: 5, items: [] });
    await settle();
    expect(service.unreadCount()).toBe(5);
  });

  it('keeps the last count when a background check fails and stops cleanly', async () => {
    service.start();
    poll().flush({ unreadCount: 2, items: [] });
    await settle();
    vi.advanceTimersByTime(notificationPollMs);
    poll().flush({}, { status: 503, statusText: 'Unavailable' });
    await settle();
    expect(service.unreadCount()).toBe(2);

    service.stop();
    expect(service.unreadCount()).toBe(0);
    vi.advanceTimersByTime(notificationPollMs * 3);
    document.dispatchEvent(new Event('visibilitychange'));
    http.expectNone(request => request.url === '/api/v1/notifications');
  });

  it('loads the feed, marks everything read and reports failures', async () => {
    const load = service.load();
    const feed = poll();
    expect(feed.request.params.get('limit')).toBe('50');
    feed.flush({ unreadCount: 1, items: [unread, { ...unread, id: 'n0', isRead: true }] });
    await load;
    expect(service.state()).toBe('loaded');
    expect(service.items()).toHaveLength(2);
    expect(service.unreadCount()).toBe(1);

    const failing = service.markAllRead();
    http.expectOne('/api/v1/notifications/read').flush({}, { status: 500, statusText: 'Error' });
    expect(await failing).toBe(false);
    expect(service.markError()).toBe(true);
    expect(service.unreadCount()).toBe(1);

    const marking = service.markAllRead();
    const request = http.expectOne('/api/v1/notifications/read');
    expect(request.request.method).toBe('POST');
    request.flush(null, { status: 204, statusText: 'No Content' });
    expect(await marking).toBe(true);
    expect(service.markError()).toBe(false);
    expect(service.unreadCount()).toBe(0);
    expect(service.items().every(item => item.isRead)).toBe(true);
  });

  it('reports a feed that cannot be loaded and recovers on reload', async () => {
    const failed = service.load();
    poll().flush({}, { status: 503, statusText: 'Unavailable' });
    await failed;
    expect(service.state()).toBe('error');

    const retried = service.load();
    poll().flush({ unreadCount: 0, items: [] });
    await retried;
    expect(service.state()).toBe('loaded');
  });
});
