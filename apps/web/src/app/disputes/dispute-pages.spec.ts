import { provideHttpClient } from '@angular/common/http';
import { DATE_PIPE_DEFAULT_OPTIONS } from '@angular/common';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../app.routes';
import { AuthService } from '../auth/auth-service';
import { installDialogPolyfill } from '../testing/dialog-polyfill';

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
    transaction: {
      description: ['Woolworths groceries', 'Monthly insurance', 'Account service fee'][index],
      merchantName: index === 2 ? undefined : ['Woolworths', 'Insurance provider'][index],
      amount: '750.00', currency: 'ZAR', transactionDate: '2026-11-10T23:30:00Z',
    },
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
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter(routes),
        { provide: DATE_PIPE_DEFAULT_OPTIONS, useValue: { timezone: '+0200' } }],
    });
    http = TestBed.inject(HttpTestingController);
    const restored = TestBed.inject(AuthService).restore();
    http.expectOne('/api/v1/auth/me').flush({
      username: 'customer.demo', actorType: 'customer', roles: ['Customer'], csrfToken: 'csrf',
    });
    await restored;
  });

  afterEach(() => {
    // The evidence and support panels load on their own; these tests are about the timeline and statuses.
    http.match(request => /\/disputes\/[^/]+\/evidence$/.test(request.url))
      .forEach(request => { if (!request.cancelled) request.flush({ items: [] }); });
    http.match(request => request.url === '/api/v1/support')
      .forEach(request => { if (!request.cancelled) request.flush({}, { status: 503, statusText: 'Unavailable' }); });
    http.verify();
  });

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
    expect(harness.routeNativeElement?.textContent).toContain('Review decision: Approved after review');
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

  it('identifies a case by its transaction in both the list and details', async () => {
    const harness = await RouterTestingHarness.create('/disputes');
    http.expectOne(request => request.url === '/api/v1/disputes')
      .flush({ items: [cases[0]], pageInfo: { hasNextPage: false } });
    harness.detectChanges();
    const card = harness.routeNativeElement!.querySelector('.case-card')!;
    expect(card.textContent).toContain('Woolworths groceries');
    expect(card.textContent).toContain('ZAR 750.00');
    expect(card.textContent).toContain('Nov 11, 2026');

    await harness.navigateByUrl(`/disputes/${cases[0].id}`);
    http.expectOne(`/api/v1/disputes/${cases[0].id}`).flush(cases[0]);
    harness.detectChanges();
    const summary = harness.routeNativeElement!.querySelector('[aria-label="Case summary"]')!;
    expect(summary.textContent).toContain('Woolworths groceries');
    expect(summary.textContent).toContain('ZAR 750.00');
    expect(summary.textContent).toContain('Nov 11, 2026');
  });

  it('uses event-specific milestones, honest next steps and local time', async () => {
    const harness = await RouterTestingHarness.create(`/disputes/${cases[0].id}`);
    http.expectOne(`/api/v1/disputes/${cases[0].id}`).flush({
      ...cases[0], decisionStatus: 'Pending', financialStatus: 'NotInitiated',
      chargeback: { reference: 'MOCK-CB-test', reasonCode: 'duplicateCharge',
        status: 'MerchantContested', lastOperationOutcome: 'Succeeded' },
      timeline: [
        { ...cases[0].timeline[0], occurredAt: '2026-11-12T23:30:00Z' },
        { ...cases[0].timeline[1], eventType: 'ChargebackForwardToAcquirer' },
        { ...cases[0].timeline[1], id: 'event-3', sequenceNumber: 3, eventType: 'ChargebackMerchantContest' },
      ],
    });
    harness.detectChanges();
    const element = harness.routeNativeElement!;
    expect(Array.from(element.querySelectorAll('ol li h3'), heading => heading.textContent?.trim()))
      .toEqual(['Dispute received', 'Sent to the merchant', 'Merchant challenged the dispute']);
    expect(element.querySelector('ol time')?.textContent).toContain('13 Nov 2026, 01:30');
    expect(element.querySelector('ol time')?.getAttribute('datetime')).toBe('2026-11-12T23:30:00Z');
    expect(element.textContent).toContain('What happens next?');
    expect(element.textContent).toContain('No completion date has been confirmed.');
    expect(element.textContent).toContain('Awaiting a decision');
    expect(element.textContent).not.toContain('Financial status: Not initiated');
  });

  it('refreshes automatically only while visible, preserves focus, and stops on navigation', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
    try {
      const harness = await RouterTestingHarness.create(`/disputes/${cases[0].id}`);
      http.expectOne(`/api/v1/disputes/${cases[0].id}`).flush(cases[0]);
      harness.detectChanges();
      const heading = harness.routeNativeElement!.querySelector<HTMLHeadingElement>('h1')!;
      const focus = vi.spyOn(heading, 'focus');
      const refreshButton = harness.routeNativeElement!.querySelector<HTMLButtonElement>('[data-testid="refresh-dispute"]')!;
      refreshButton.focus();
      vi.advanceTimersByTime(29_999);
      http.expectNone(`/api/v1/disputes/${cases[0].id}`);
      vi.advanceTimersByTime(1);
      const refresh = http.expectOne(`/api/v1/disputes/${cases[0].id}`);
      harness.detectChanges();
      expect(refreshButton.disabled).toBe(false);
      expect(refreshButton.getAttribute('aria-disabled')).toBe('true');
      refreshButton.click();
      http.expectNone(`/api/v1/disputes/${cases[0].id}`);
      vi.advanceTimersByTime(30_000);
      http.expectNone(`/api/v1/disputes/${cases[0].id}`);
      refresh.flush({ ...cases[0], financialStatus: 'OutcomeUnknown' });
      harness.detectChanges();
      expect(harness.routeNativeElement?.textContent).toContain('Outcome unknown - not confirmed');
      expect(focus).not.toHaveBeenCalled();
      expect(document.activeElement).toBe(refreshButton);

      visibility.mockReturnValue('hidden');
      vi.advanceTimersByTime(30_000);
      http.expectNone(`/api/v1/disputes/${cases[0].id}`);
      visibility.mockReturnValue('visible');
      vi.advanceTimersByTime(30_000);
      http.expectOne(`/api/v1/disputes/${cases[0].id}`)
        .flush({}, { status: 503, statusText: 'Unavailable' });
      harness.detectChanges();
      expect(harness.routeNativeElement?.textContent).toContain(cases[0].reference);
      expect(harness.routeNativeElement?.querySelector('[role="alert"]')?.textContent).toContain('last loaded case');

      await harness.navigateByUrl('/disputes');
      http.expectOne(request => request.url === '/api/v1/disputes')
        .flush({ items: [], pageInfo: { hasNextPage: false } });
      vi.advanceTimersByTime(30_000);
      http.expectNone(`/api/v1/disputes/${cases[0].id}`);
    } finally {
      visibility.mockRestore();
      vi.useRealTimers();
    }
  });

  it('keeps decided cases in progress until closure, then stops automatic checks', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
    try {
      const harness = await RouterTestingHarness.create('/disputes');
      http.expectOne(request => request.url === '/api/v1/disputes').flush({
        items: cases.map((item, index) => ({ ...item, caseStatus: ['Rejected', 'ReversalConfirmed', 'Closed'][index] })),
        pageInfo: { hasNextPage: false },
      });
      harness.detectChanges();
      expect(harness.routeNativeElement!.querySelector('cc-stat[label="In progress"]')?.textContent).toContain('2');
      expect(harness.routeNativeElement!.querySelector('cc-stat[label="Closed cases"]')?.textContent).toContain('1');
      await harness.navigateByUrl(`/disputes/${cases[0].id}`);
      http.expectOne(`/api/v1/disputes/${cases[0].id}`)
        .flush({ ...cases[0], caseStatus: 'Rejected', decisionStatus: 'Rejected', financialStatus: 'NotInitiated' });
      harness.detectChanges();
      expect(harness.routeNativeElement?.textContent).toContain('awaiting closure');
      vi.advanceTimersByTime(30_000);
      http.expectOne(`/api/v1/disputes/${cases[0].id}`)
        .flush({ ...cases[0], caseStatus: 'Closed', decisionStatus: 'Rejected', financialStatus: 'NotInitiated' });
      harness.detectChanges();
      expect(harness.routeNativeElement?.textContent).toContain('This case is closed');
      expect(harness.routeNativeElement?.textContent).not.toContain('No completion date has been confirmed');
      vi.advanceTimersByTime(60_000);
      http.expectNone(`/api/v1/disputes/${cases[0].id}`);
    } finally {
      visibility.mockRestore();
      vi.useRealTimers();
    }
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

    expect(harness.routeNativeElement?.textContent).toContain('Refund requested');
    expect(harness.routeNativeElement?.textContent).toContain('Review decision: Approved after review');
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
    expect(harness.routeNativeElement?.textContent).toContain('Review decision: Approved after review');
    expect(harness.routeNativeElement?.textContent).toContain('Money update: Not started');
    expect(harness.routeNativeElement?.textContent).not.toContain('refund confirmed');
    harness.routeNativeElement!.querySelector<HTMLButtonElement>('[data-testid="refresh-dispute"]')!.click();
    http.expectOne(`/api/v1/disputes/${cases[0].id}`).flush({ ...accepted, financialStatus: 'Confirmed' });
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('Demo refund confirmed - no real money moved');
    expect(harness.routeNativeElement?.textContent).not.toContain('reversal confirmed');
    http.expectNone(req => req.url.includes('simulations'));
  });

  describe('customer actions on the dispute page', () => {
    const detailUrl = `/api/v1/disputes/${cases[0].id}`;
    const evidenceUrl = `${detailUrl}/evidence`;
    const withdrawable = { ...cases[0], version: 7, canWithdraw: true, decisionStatus: 'Pending',
      financialStatus: 'NotInitiated', caseStatus: 'Submitted' };

    beforeAll(() => installDialogPolyfill());

    async function open(dispute: object) {
      const harness = await RouterTestingHarness.create(`/disputes/${cases[0].id}`);
      http.expectOne(detailUrl).flush(dispute);
      harness.detectChanges();
      return harness;
    }

    it('shows the server expected resolution date instead of the unconfirmed text', async () => {
      const harness = await open({ ...cases[0], expectedResolutionBy: '2026-11-20T10:00:00Z' });
      const text = harness.routeNativeElement!.textContent!;
      expect(text).toContain('Expected resolution by');
      expect(harness.routeNativeElement!.querySelector('[data-testid="expected-resolution"] time')?.getAttribute('datetime'))
        .toBe('2026-11-20T10:00:00Z');
      expect(text).toContain('Nov 20, 2026');
      expect(text).not.toContain('No completion date has been confirmed');
    });

    it('lists evidence for download and shows support contact details quoting the case reference', async () => {
      const harness = await open(cases[0]);
      http.expectOne(evidenceUrl).flush({ items: [{ id: 'ev-1', fileName: 'receipt.pdf', contentType: 'application/pdf',
        sizeBytes: 2048, uploadedAt: '2026-11-13T10:00:00Z' }] });
      http.expectOne('/api/v1/support').flush({ email: 'support@clearcase.example', phone: '0800 000 000',
        hours: 'Mon-Fri 08:00-17:00 SAST', responseTime: 'Within 1 business day' });
      harness.detectChanges();
      const element = harness.routeNativeElement!;
      expect(element.querySelector('[data-testid="evidence-download"]')?.getAttribute('href'))
        .toBe(`${evidenceUrl}/ev-1/content`);
      expect(element.querySelector('[data-testid="support-email"]')?.getAttribute('href'))
        .toContain(encodeURIComponent(cases[0].reference));
      expect(element.querySelector('#support-heading')?.textContent).toContain('Contact support');
    });

    it('keeps the case usable with a plain-text fallback when evidence and support fail to load', async () => {
      const harness = await open(cases[0]);
      http.expectOne(evidenceUrl).flush({}, { status: 500, statusText: 'Error' });
      http.expectOne('/api/v1/support').flush({}, { status: 503, statusText: 'Unavailable' });
      harness.detectChanges();
      const element = harness.routeNativeElement!;
      expect(element.textContent).toContain('Unable to load your documents');
      expect(element.textContent).toContain('Support contact details are unavailable right now');
      expect(element.textContent).toContain(cases[0].reference);
    });

    it('withdraws a dispute after confirmation and shows it closed without upload controls', async () => {
      const harness = await open(withdrawable);
      http.expectOne(evidenceUrl).flush({ items: [] });
      http.expectOne('/api/v1/support').flush({}, { status: 503, statusText: 'Unavailable' });
      harness.detectChanges();
      const element = harness.routeNativeElement!;
      expect(element.querySelector('#evidence-file')).not.toBeNull();
      element.querySelector<HTMLButtonElement>('[data-testid="withdraw-dispute"]')!.click();
      await harness.fixture.whenStable();
      expect(element.querySelector('dialog')?.hasAttribute('open')).toBe(true);

      element.querySelector<HTMLFormElement>('dialog form')!.dispatchEvent(new Event('submit', { cancelable: true }));
      const request = http.expectOne(`${detailUrl}/withdrawals`);
      expect(request.request.body).toEqual({ expectedVersion: 7 });
      request.flush({ ...withdrawable, caseStatus: 'Closed', canWithdraw: false, version: 8,
        expectedResolutionBy: undefined });
      await harness.fixture.whenStable();
      harness.detectChanges();

      expect(element.querySelector('[data-testid="withdraw-dispute"]')).toBeNull();
      expect(element.querySelector('[data-testid="withdraw-outcome"]')?.textContent)
        .toContain('You withdrew this dispute. No payment adjustment was made.');
      expect(element.textContent).toContain('This case is closed');
      expect(element.querySelector('#evidence-file')).toBeNull();
      expect(element.querySelector('[data-testid="evidence-closed"]')).not.toBeNull();
    });

    it('refreshes the case after a stale-version withdrawal and explains what happened', async () => {
      const harness = await open(withdrawable);
      http.expectOne(evidenceUrl).flush({ items: [] });
      http.expectOne('/api/v1/support').flush({}, { status: 503, statusText: 'Unavailable' });
      harness.detectChanges();
      const element = harness.routeNativeElement!;
      element.querySelector<HTMLButtonElement>('[data-testid="withdraw-dispute"]')!.click();
      await harness.fixture.whenStable();
      element.querySelector<HTMLFormElement>('dialog form')!.dispatchEvent(new Event('submit', { cancelable: true }));
      http.expectOne(`${detailUrl}/withdrawals`)
        .flush({ code: 'dispute.concurrencyConflict' }, { status: 409, statusText: 'Conflict' });
      await harness.fixture.whenStable();

      http.expectOne(detailUrl).flush({ ...withdrawable, version: 8, canWithdraw: false, caseStatus: 'UnderReview' });
      harness.detectChanges();
      expect(element.querySelector('[data-testid="withdraw-dispute"]')).toBeNull();
      expect(element.querySelector('[data-testid="withdraw-outcome"]')?.textContent).toContain('changed while you were viewing it');
      expect(element.textContent).toContain('Under review');
    });
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
