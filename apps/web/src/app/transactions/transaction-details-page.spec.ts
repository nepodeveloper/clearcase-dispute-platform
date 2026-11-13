import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../app.routes';
import { TransactionDetailsPage } from './transaction-details-page';
import { TransactionsPage } from './transactions-page';
import { AuthService } from '../auth/auth-service';

describe('TransactionDetailsPage', () => {
  const id = '00000000-0000-0000-0000-000000000001';
  const transaction = {
    id,
    transactionType: 'CardPurchase',
    description: 'Woolworths',
    merchantName: 'Woolworths',
    amount: '750.00',
    currency: 'ZAR',
    transactionDate: '2026-11-10T08:30:00Z',
    status: 'Posted',
    reference: 'TXN-2026-000001',
    isDisputable: true,
  };
  const eligibility = {
    transactionId: id,
    outcome: 'EligibleToSubmit',
    reasonCodes: [],
    explanation: 'This transaction can be submitted for review.',
    policyVersion: 'demo-v1',
    evaluatedAt: '2026-11-12T10:00:00Z',
    availableReasons: [
      { code: 'unrecognisedTransaction', label: 'I do not recognise this transaction' },
      { code: 'duplicateCharge', label: 'I was charged more than once' },
    ],
  };
  let http: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(routes),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const restored = TestBed.inject(AuthService).restore();
    http.expectOne('/api/v1/auth/me').flush({
      username: 'customer.demo', actorType: 'customer', roles: ['Customer'], csrfToken: 'csrf',
    });
    await restored;
  });

  afterEach(() => http.verify());

  it('shows loading, requests the transaction and renders its details', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(`/transactions/${id}`, TransactionDetailsPage);
    expect(harness.routeNativeElement?.textContent).toContain('Loading transaction');
    const request = http.expectOne(`/api/v1/transactions/${id}`);
    expect(request.request.method).toBe('GET');
    request.flush(transaction);
    expectEligibility();
    harness.detectChanges();

    const text = harness.routeNativeElement?.textContent ?? '';
    expect(text).toContain('Woolworths');
    expect(text).toContain('Card purchase');
    expect(text).toContain('750.00');
    expect(text).toContain('ZAR');
    expect(text).toContain('Nov 10, 2026');
    expect(text).toContain('TXN-2026-000001');
    expect(text).toContain('Posted');
    expect(text).toContain('Eligible to submit a dispute.');
    expect(text).toContain(eligibility.explanation);
    expect(harness.routeNativeElement?.querySelector('[data-testid="submit-dispute"]')).not.toBeNull();
    expect(text).toContain('I do not recognise this transaction');
  });

  it('renders manual review and its customer-safe explanation', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(`/transactions/${id}`, TransactionDetailsPage);
    http.expectOne(`/api/v1/transactions/${id}`).flush(transaction);
    http.expectOne(`/api/v1/transactions/${id}/dispute-eligibility`).flush({
      ...eligibility,
      outcome: 'RequiresManualReview',
      explanation: 'This amount needs enhanced review; the amount alone does not prevent you from raising a dispute.',
    });
    harness.detectChanges();

    const text = harness.routeNativeElement?.textContent ?? '';
    expect(text).toContain('Requires further review.');
    expect(text).toContain('the amount alone does not prevent you from raising a dispute');
    expect(harness.routeNativeElement?.querySelector('[data-testid="submit-dispute"]')).not.toBeNull();
  });

  it('renders ineligible outcome and its customer-safe explanation', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(`/transactions/${id}`, TransactionDetailsPage);
    http.expectOne(`/api/v1/transactions/${id}`).flush(transaction);
    http.expectOne(`/api/v1/transactions/${id}/dispute-eligibility`).flush({
      ...eligibility,
      outcome: 'Ineligible',
      explanation: 'This card transaction is still a pending authorization.',
      availableReasons: [],
    });

    harness.detectChanges();

    const text = harness.routeNativeElement?.textContent ?? '';
    expect(text).toContain('Currently ineligible.');
    expect(text).toContain('This card transaction is still a pending authorization.');
    expect(harness.routeNativeElement?.querySelector('[data-testid="submit-dispute"]')).toBeNull();
  });

  it('keeps the investigation form available when ordinary DebiCheck reversal is ineligible', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(`/transactions/${id}`, TransactionDetailsPage);
    http.expectOne(`/api/v1/transactions/${id}`).flush({ ...transaction, transactionType: 'DebitOrder' });
    http.expectOne(`/api/v1/transactions/${id}/dispute-eligibility`).flush({
      ...eligibility,
      outcome: 'RequiresManualReview',
      ordinaryReversalOutcome: 'Ineligible',
      reviewRoute: 'Investigation',
      explanation: 'Ordinary reversal is unavailable. Disputed authorisation requires investigation.',
      availableReasons: [{ code: 'unauthorisedDebitOrder', label: 'I did not authorise this debit order' }],
    });
    harness.detectChanges();

    const text = harness.routeNativeElement?.textContent ?? '';
    expect(text).toContain('Disputed authorisation requires investigation.');
    expect(text).toContain('I did not authorise this debit order');
    expect(harness.routeNativeElement?.querySelector('[data-testid="submit-dispute"]')).not.toBeNull();
  });

  it('shows eligibility loading and error states and retries the check', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(`/transactions/${id}`, TransactionDetailsPage);
    http.expectOne(`/api/v1/transactions/${id}`).flush(transaction);
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('Checking dispute eligibility');

    http.expectOne(`/api/v1/transactions/${id}/dispute-eligibility`)
      .flush({}, { status: 503, statusText: 'Service Unavailable' });
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('Unable to check dispute eligibility');

    (harness.routeNativeElement?.querySelector('[data-testid="retry-eligibility"]') as HTMLButtonElement).click();
    http.expectOne(`/api/v1/transactions/${id}`).flush(transaction);
    http.expectOne(`/api/v1/transactions/${id}/dispute-eligibility`).flush(eligibility);
    harness.detectChanges();

    expect(harness.routeNativeElement?.textContent).toContain('Eligible to submit a dispute.');
  });

  it('shows a not-found state for a 404', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(`/transactions/${id}`, TransactionDetailsPage);
    http.expectOne(`/api/v1/transactions/${id}`).flush({}, { status: 404, statusText: 'Not Found' });
    harness.detectChanges();

    expect(harness.routeNativeElement?.textContent).toContain('Transaction not found');
    expect(harness.routeNativeElement?.querySelector('[data-testid="retry"]')).toBeNull();
  });

  it('shows a recoverable network error and retries', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(`/transactions/${id}`, TransactionDetailsPage);
    http.expectOne(`/api/v1/transactions/${id}`).error(new ProgressEvent('error'));
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('Unable to load transaction');

    (harness.routeNativeElement?.querySelector('[data-testid="retry"]') as HTMLButtonElement).click();
    http.expectOne(`/api/v1/transactions/${id}`).flush(transaction);
    expectEligibility();
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('TXN-2026-000001');
  });

  it('explains when authentication is required without offering a network retry', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(`/transactions/${id}`, TransactionDetailsPage);
    http.expectOne(`/api/v1/transactions/${id}`).flush({}, { status: 401, statusText: 'Unauthorized' });
    harness.detectChanges();

    expect(harness.routeNativeElement?.textContent).toContain('You are not signed in');
    expect(harness.routeNativeElement?.querySelector('[data-testid="retry"]')).toBeNull();
  });

  it('displays unavailable optional details clearly', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(`/transactions/${id}`, TransactionDetailsPage);
    const { merchantName, reference, ...withoutOptionalDetails } = transaction;
    http.expectOne(`/api/v1/transactions/${id}`).flush(withoutOptionalDetails);
    expectEligibility();
    harness.detectChanges();

    expect(harness.routeNativeElement?.textContent?.match(/Not available/g)?.length).toBe(2);
  });

  it('navigates back to the transaction list', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(`/transactions/${id}`, TransactionDetailsPage);
    http.expectOne(`/api/v1/transactions/${id}`).flush(transaction);
    expectEligibility();
    harness.detectChanges();
    (harness.routeNativeElement?.querySelector('[data-testid="back-to-transactions"]') as HTMLAnchorElement).click();
    await harness.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/');
    http.expectOne((request) => request.url === '/api/v1/transactions')
      .flush({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0, totalPages: 0, hasNextPage: false });
  });

  it('opens details when a transaction is selected in the list', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/', TransactionsPage);
    http.expectOne((request) => request.url === '/api/v1/transactions')
      .flush({ items: [transaction], pageNumber: 1, pageSize: 20, totalCount: 1, totalPages: 1, hasNextPage: false });
    harness.detectChanges();
    (harness.routeNativeElement?.querySelector('[data-testid="transaction-link"]') as HTMLAnchorElement).click();
    await harness.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe(`/transactions/${id}`);
    http.expectOne(`/api/v1/transactions/${id}`).flush(transaction);
    expectEligibility();
  });

  function expectEligibility(result = eligibility): void {
    const request = http.expectOne(`/api/v1/transactions/${id}/dispute-eligibility`);
    expect(request.request.method).toBe('GET');
    request.flush(result);
  }
});
