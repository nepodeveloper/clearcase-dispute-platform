import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../app.routes';
import { AuthService } from '../auth/auth-service';

describe('Dispute tracking pages', () => {
  const cases = ['CardPurchase', 'DebitOrder', 'BankCharge'].map((category, index) => ({
    id: `aaaaaaaa-1111-4222-8333-44445555666${index}`,
    reference: `DSP-00000000000${index}`,
    transactionId: `00000000-0000-0000-0000-00000000000${index + 1}`,
    category,
    reasonCode: 'incorrectFee',
    description: 'Please explain this transaction.',
    caseStatus: 'UnderReview',
    decisionStatus: 'Approved',
    financialStatus: 'Requested',
    statusDescription: 'A simulated adjustment was requested; funds are not confirmed.',
    createdAt: '2026-11-12T10:00:00Z',
    timeline: [
      { id: 'event-1', sequenceNumber: 1, eventType: 'DisputeSubmitted', caseStatus: 'Submitted',
        description: 'Your dispute was received.', occurredAt: '2026-11-12T10:00:00Z' },
      { id: 'event-2', sequenceNumber: 2, eventType: 'UnderReview', caseStatus: 'UnderReview',
        description: 'Your dispute is under review.', occurredAt: '2026-11-13T10:00:00Z' },
    ],
  }));
  let http: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter(routes)],
    });
    http = TestBed.inject(HttpTestingController);
    const restored = TestBed.inject(AuthService).restore();
    http.expectOne('/api/v1/auth/me').flush({
      username: 'customer.demo', actorType: 'customer', roles: ['Customer'], csrfToken: 'csrf',
    });
    await restored;
  });

  afterEach(() => http.verify());

  it('shows loading and an accessible empty state linking to transactions', async () => {
    const harness = await RouterTestingHarness.create('/disputes');
    expect(harness.routeNativeElement?.textContent).toContain('Loading disputes');
    const request = http.expectOne((req) => req.url === '/api/v1/disputes');
    expect(request.request.params.get('pageSize')).toBe('20');
    request.flush({ items: [], pageInfo: { hasNextPage: false } });
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('No disputes submitted');
    expect(harness.routeNativeElement?.querySelector('a[href="/"]')).not.toBeNull();
  });

  it('renders all categories and navigates to case details and back', async () => {
    const harness = await RouterTestingHarness.create('/disputes');
    http.expectOne((req) => req.url === '/api/v1/disputes')
      .flush({ items: cases, pageInfo: { hasNextPage: false } });
    harness.detectChanges();
    const text = harness.routeNativeElement?.textContent ?? '';
    for (const category of ['Card purchase', 'Debit order', 'Bank charge']) expect(text).toContain(category);
    expect(text).toContain('funds are not confirmed');
    expect(harness.routeNativeElement?.querySelector('.case-outcomes')?.textContent).toContain('Approved');
    expect(harness.routeNativeElement?.querySelector('.case-outcomes')?.textContent).toContain('Requested - not confirmed');
    (harness.routeNativeElement?.querySelector(`a[href="/disputes/${cases[2].id}"]`) as HTMLAnchorElement).click();
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe(`/disputes/${cases[2].id}`);
    http.expectOne(`/api/v1/disputes/${cases[2].id}`).flush(cases[2]);
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('Decision: Approved');
    expect(harness.routeNativeElement?.textContent).toContain('Requested - not confirmed');
    const timeline = harness.routeNativeElement?.querySelector('ol[aria-label="Dispute timeline"]');
    expect(timeline?.querySelectorAll('li')).toHaveLength(2);
    expect(Array.from(timeline?.querySelectorAll('time') ?? []).map((item) => item.getAttribute('datetime')))
      .toEqual(['2026-11-12T10:00:00Z', '2026-11-13T10:00:00Z']);
    (harness.routeNativeElement?.querySelector('[data-testid="back-to-disputes"]') as HTMLAnchorElement).click();
    await harness.fixture.whenStable();
    http.expectOne((req) => req.url === '/api/v1/disputes')
      .flush({ items: cases, pageInfo: { hasNextPage: false } });
    expect(TestBed.inject(Router).url).toBe('/disputes');
  });

  it('retries a dashboard error and a failed next page without losing existing cases', async () => {
    const harness = await RouterTestingHarness.create('/disputes');
    http.expectOne((req) => req.url === '/api/v1/disputes')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    harness.detectChanges();
    expect(harness.routeNativeElement?.querySelector('[role="alert"]')?.textContent).toContain('Unable to load disputes');
    (harness.routeNativeElement?.querySelector('[data-testid="retry"]') as HTMLButtonElement).click();
    http.expectOne((req) => req.url === '/api/v1/disputes')
      .flush({ items: [cases[0]], pageInfo: { hasNextPage: true, nextCursor: 'opaque-cursor' } });
    harness.detectChanges();
    (harness.routeNativeElement?.querySelector('[data-testid="more-disputes"]') as HTMLButtonElement).click();
    const failed = http.expectOne((req) => req.url === '/api/v1/disputes');
    expect(failed.request.params.get('cursor')).toBe('opaque-cursor');
    failed.flush({}, { status: 503, statusText: 'Unavailable' });
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain(cases[0].reference);
    expect(harness.routeNativeElement?.querySelector('[role="alert"]')).not.toBeNull();
    (harness.routeNativeElement?.querySelector('[data-testid="more-disputes"]') as HTMLButtonElement).click();
    http.expectOne((req) => req.url === '/api/v1/disputes')
      .flush({ items: [cases[1]], pageInfo: { hasNextPage: false } });
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain(cases[1].reference);
    expect(harness.routeNativeElement?.querySelector('[data-testid="more-disputes"]')).toBeNull();
  });

  it('cancels an old detail request when navigating to a different case', async () => {
    const harness = await RouterTestingHarness.create(`/disputes/${cases[0].id}`);
    const first = http.expectOne(`/api/v1/disputes/${cases[0].id}`);
    await harness.navigateByUrl(`/disputes/${cases[1].id}`);
    expect(first.cancelled).toBe(true);
    http.expectOne(`/api/v1/disputes/${cases[1].id}`).flush(cases[1]);
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain(cases[1].reference);
    expect(harness.routeNativeElement?.textContent).not.toContain(cases[0].reference);
  });

  it('orders the public timeline by sequence even with tied or reversed timestamps', async () => {
    const harness = await RouterTestingHarness.create(`/disputes/${cases[0].id}`);
    http.expectOne(`/api/v1/disputes/${cases[0].id}`).flush({
      ...cases[0],
      internalNotes: 'Never show investigation notes',
      timeline: [
        { ...cases[0].timeline[1], occurredAt: '2026-11-11T10:00:00Z', internalNotes: 'Staff only' },
        cases[0].timeline[0],
        { ...cases[0].timeline[1], id: 'event-3', sequenceNumber: 3, description: 'Latest event.',
          occurredAt: cases[0].timeline[0].occurredAt },
      ],
    });
    harness.detectChanges();

    expect(Array.from(harness.routeNativeElement!.querySelectorAll('ol li p'), item => item.textContent?.trim()))
      .toEqual(['Your dispute was received.', 'Your dispute is under review.', 'Latest event.']);
    expect(harness.routeNativeElement?.textContent).not.toContain('Never show investigation notes');
    expect(harness.routeNativeElement?.textContent).not.toContain('Staff only');
  });

  it('refreshes processing and timeline without implying approved funds are confirmed', async () => {
    const harness = await RouterTestingHarness.create(`/disputes/${cases[1].id}`);
    http.expectOne(`/api/v1/disputes/${cases[1].id}`).flush({ ...cases[1], caseStatus: 'ManualReview' });
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('Referred for specialist review');
    harness.routeNativeElement!.querySelector<HTMLButtonElement>('[data-testid="refresh-dispute"]')!.click();
    const request = http.expectOne(`/api/v1/disputes/${cases[1].id}`);
    expect(request.request.method).toBe('GET');
    request.flush({ ...cases[1], caseStatus: 'ReversalRequested', financialStatus: 'OutcomeUnknown' });
    harness.detectChanges();

    expect(harness.routeNativeElement?.textContent).toContain('Reversal requested');
    expect(harness.routeNativeElement?.textContent).toContain('Decision: Approved');
    expect(harness.routeNativeElement?.textContent).toContain('Outcome unknown - not confirmed');
    expect(harness.routeNativeElement?.querySelectorAll('ol li')).toHaveLength(2);
    http.expectNone(req => req.url.includes('simulations'));
  });

  it('keeps the case visible and allows retry when status refresh fails', async () => {
    const harness = await RouterTestingHarness.create(`/disputes/${cases[0].id}`);
    http.expectOne(`/api/v1/disputes/${cases[0].id}`).flush(cases[0]);
    harness.detectChanges();
    harness.routeNativeElement!.querySelector<HTMLButtonElement>('[data-testid="refresh-dispute"]')!.click();
    http.expectOne(`/api/v1/disputes/${cases[0].id}`).flush({}, { status: 503, statusText: 'Unavailable' });
    harness.detectChanges();

    expect(harness.routeNativeElement?.textContent).toContain(cases[0].reference);
    expect(harness.routeNativeElement?.querySelector('[role="alert"]')?.textContent).toContain('Unable to load');
    harness.routeNativeElement!.querySelector<HTMLButtonElement>('[data-testid="refresh-dispute"]')!.click();
    http.expectOne(`/api/v1/disputes/${cases[0].id}`).flush(cases[0]);
    harness.detectChanges();
    expect(harness.routeNativeElement?.querySelector('[role="alert"]')).toBeNull();
  });

  it('shows chargeback acceptance separately from explicit simulated settlement', async () => {
    const accepted = { ...cases[0], financialStatus: 'NotInitiated',
      chargeback: { reference: 'MOCK-CB-test', reasonCode: 'duplicateCharge',
        status: 'MerchantAccepted', lastOperationOutcome: 'Succeeded' } };
    const harness = await RouterTestingHarness.create(`/disputes/${cases[0].id}`);
    http.expectOne(`/api/v1/disputes/${cases[0].id}`).flush(accepted);
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('MOCK-CB-test');
    expect(harness.routeNativeElement?.textContent).toContain('Merchant accepted');
    expect(harness.routeNativeElement?.textContent).toContain('Decision: Approved');
    expect(harness.routeNativeElement?.textContent).toContain('Financial status: Not initiated');
    expect(harness.routeNativeElement?.textContent).not.toContain('settlement confirmed');
    harness.routeNativeElement!.querySelector<HTMLButtonElement>('[data-testid="refresh-dispute"]')!.click();
    http.expectOne(`/api/v1/disputes/${cases[0].id}`).flush({ ...accepted, financialStatus: 'Confirmed' });
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('Simulated settlement confirmed - no real money moved');
    expect(harness.routeNativeElement?.textContent).not.toContain('reversal confirmed');
    http.expectNone(req => req.url.includes('simulations'));
  });

  it('handles missing cases and retries transient detail errors', async () => {
    const harness = await RouterTestingHarness.create(`/disputes/${cases[0].id}`);
    http.expectOne(`/api/v1/disputes/${cases[0].id}`).flush({}, { status: 404, statusText: 'Not Found' });
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('Dispute not found');
    expect(harness.routeNativeElement?.querySelector('[data-testid="retry"]')).toBeNull();
    await harness.navigateByUrl(`/disputes/${cases[1].id}`);
    http.expectOne(`/api/v1/disputes/${cases[1].id}`).flush({}, { status: 503, statusText: 'Unavailable' });
    harness.detectChanges();
    (harness.routeNativeElement?.querySelector('[data-testid="retry"]') as HTMLButtonElement).click();
    http.expectOne(`/api/v1/disputes/${cases[1].id}`).flush(cases[1]);
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain(cases[1].reference);
  });
});
