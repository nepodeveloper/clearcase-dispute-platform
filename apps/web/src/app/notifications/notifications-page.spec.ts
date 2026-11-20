import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { NotificationsPage } from './notifications-page';

describe('NotificationsPage', () => {
  const feed = {
    unreadCount: 2,
    items: [
      { id: 'n2', disputeId: 'dispute-2', disputeReference: 'DSP-2', eventType: 'ReversalRequested',
        message: 'A refund was requested.', occurredAt: '2026-11-14T10:00:00Z', isRead: false },
      { id: 'n1', disputeId: 'dispute-1', disputeReference: 'DSP-1', eventType: 'UnderReview',
        message: 'Your dispute is under review.', occurredAt: '2026-11-13T10:00:00Z', isRead: false },
      { id: 'n0', disputeId: 'dispute-1', disputeReference: 'DSP-1', eventType: 'DisputeSubmitted',
        message: 'Your dispute was received.', occurredAt: '2026-11-12T10:00:00Z', isRead: true },
    ],
  };
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [NotificationsPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function render() {
    const fixture = TestBed.createComponent(NotificationsPage);
    fixture.detectChanges();
    return fixture;
  }

  const load = () => http.expectOne(request => request.url === '/api/v1/notifications');

  it('lists notifications with unread styling and text, links to the dispute and announces the unread count', async () => {
    const fixture = render();
    load().flush(feed);
    await fixture.whenStable();
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const items = root.querySelectorAll('li.notification');
    expect(items).toHaveLength(3);
    expect(items[0].classList.contains('is-unread')).toBe(true);
    expect(items[0].querySelector('[data-testid="unread-tag"]')?.textContent).toContain('New');
    expect(items[2].classList.contains('is-unread')).toBe(false);
    expect(items[2].querySelector('[data-testid="unread-tag"]')).toBeNull();
    expect(items[0].querySelector('a')?.getAttribute('href')).toBe('/disputes/dispute-2');
    expect(items[1].textContent).toContain('Your dispute is under review.');
    const status = root.querySelector('[data-testid="notification-status"]')!;
    expect(status.getAttribute('role')).toBe('status');
    expect(status.textContent).toContain('2 unread notifications.');
  });

  it('marks all as read, updates the list and announces the result', async () => {
    const fixture = render();
    load().flush(feed);
    await fixture.whenStable();
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const button = root.querySelector<HTMLButtonElement>('[data-testid="mark-all-read"]')!;
    expect(button.disabled).toBe(false);
    button.click();
    const request = http.expectOne('/api/v1/notifications/read');
    expect(request.request.method).toBe('POST');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.querySelectorAll('li.is-unread')).toHaveLength(0);
    expect(root.querySelector('[data-testid="notification-status"]')?.textContent).toContain('All notifications marked as read.');
    expect(button.disabled).toBe(true);
  });

  it('shows an alert when marking fails and leaves the unread items alone', async () => {
    const fixture = render();
    load().flush(feed);
    await fixture.whenStable();
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    root.querySelector<HTMLButtonElement>('[data-testid="mark-all-read"]')!.click();
    http.expectOne('/api/v1/notifications/read').flush({}, { status: 500, statusText: 'Error' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Unable to mark notifications as read');
    expect(root.querySelectorAll('li.is-unread')).toHaveLength(2);
  });

  it('shows an empty state', async () => {
    const fixture = render();
    load().flush({ unreadCount: 0, items: [] });
    await fixture.whenStable();
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('No notifications yet');
    expect(root.querySelector('[data-testid="notification-status"]')?.textContent).toContain('all caught up');
    expect(root.querySelector<HTMLButtonElement>('[data-testid="mark-all-read"]')!.disabled).toBe(true);
  });

  it('offers a retry when the feed cannot be loaded', async () => {
    const fixture = render();
    load().flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Unable to load notifications');
    root.querySelector<HTMLButtonElement>('[data-testid="retry"]')!.click();
    load().flush(feed);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.querySelectorAll('li.notification')).toHaveLength(3);
  });
});
