import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TransactionsPage } from './transactions-page';

describe('TransactionsPage', () => {
  const item = {
    id: '00000000-0000-0000-0000-000000000001',
    transactionType: 'CardPurchase',
    description: 'Woolworths',
    amount: '750.00',
    currency: 'ZAR',
    transactionDate: '2026-11-10T08:30:00Z',
    status: 'Posted',
    isDisputable: true,
  };
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TransactionsPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function render() {
    const fixture = TestBed.createComponent(TransactionsPage);
    fixture.detectChanges();
    return fixture;
  }

  function respond(items = [item], hasNextPage = false) {
    http.expectOne((request) => request.url === '/api/v1/transactions').flush({
      items, pageNumber: 1, pageSize: 20, totalCount: items.length, totalPages: hasNextPage ? 2 : 1, hasNextPage,
    });
  }

  it('shows loading and renders transactions', () => {
    const fixture = render();
    expect(fixture.nativeElement.textContent).toContain('Loading transactions');
    respond();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Woolworths');
    expect(fixture.nativeElement.textContent).toContain('Card purchase');
    expect(fixture.nativeElement.textContent).toContain('750.00');
    expect(fixture.nativeElement.textContent).toContain('Posted');
  });

  it('shows an empty state', () => {
    const fixture = render();
    respond([]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No transactions found');
  });

  it('preserves decimal cents beyond JavaScript number precision and makes the table keyboard scrollable', () => {
    const fixture = render();
    respond([{ ...item, amount: '90071992547409.91' }]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('ZAR 90,071,992,547,409.91');
    const region = fixture.nativeElement.querySelector('.cc-table-scroll');
    expect(region.getAttribute('tabindex')).toBe('0');
    expect(region.getAttribute('aria-label')).toContain('transactions');
  });

  it('retries the failed request', () => {
    const fixture = render();
    http.expectOne((request) => request.url === '/api/v1/transactions').flush('error', { status: 500, statusText: 'Error' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Unable to load transactions');
    fixture.nativeElement.querySelector('[data-testid="retry"]').click();
    respond();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Woolworths');
  });

  it('requests the next page', () => {
    const fixture = render();
    respond([item], true);
    fixture.detectChanges();
    fixture.nativeElement.querySelector('[data-testid="next"]').click();
    const request = http.expectOne((req) => req.url === '/api/v1/transactions');
    expect(request.request.params.get('pageNumber')).toBe('2');
    request.flush({ items: [item], pageNumber: 2, pageSize: 20, totalCount: 21, totalPages: 2, hasNextPage: false });
  });

  it('moves focus to the transaction heading after next and previous page loads', () => {
    const fixture = render();
    respond([item], true);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const heading = element.querySelector<HTMLElement>('#transactions-heading')!;
    const next = element.querySelector<HTMLButtonElement>('[data-testid="next"]')!;
    next.focus();
    next.click();
    http.expectOne((request) => request.url === '/api/v1/transactions')
      .flush({ items: [item], pageNumber: 2, pageSize: 20, totalCount: 21, totalPages: 2, hasNextPage: false });
    fixture.detectChanges();
    expect(document.activeElement).toBe(heading);

    const previous = element.querySelector<HTMLButtonElement>('.pagination button')!;
    previous.focus();
    previous.click();
    http.expectOne((request) => request.url === '/api/v1/transactions')
      .flush({ items: [item], pageNumber: 1, pageSize: 20, totalCount: 21, totalPages: 2, hasNextPage: true });
    fixture.detectChanges();
    expect(document.activeElement).toBe(heading);
  });

  it('explains an invalid date range without sending it and allows correction', () => {
    const fixture = render();
    respond();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const from = element.querySelector<HTMLInputElement>('[name="fromDate"]')!;
    const to = element.querySelector<HTMLInputElement>('[name="toDate"]')!;
    const form = element.querySelector<HTMLFormElement>('form')!;
    from.value = '2026-11-10';
    from.dispatchEvent(new Event('input'));
    to.value = '2026-11-09';
    to.dispatchEvent(new Event('input'));
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    http.expectNone((request) => request.url === '/api/v1/transactions');
    expect(element.textContent).toContain('To date must not precede from date.');
    expect(to.getAttribute('aria-invalid')).toBe('true');
    expect(to.getAttribute('aria-describedby')).toBe('date-range-error');
    expect(element.querySelector('[data-testid="retry"]')).toBeNull();

    to.value = '2026-11-11';
    to.dispatchEvent(new Event('input'));
    form.dispatchEvent(new Event('submit'));
    const request = http.expectOne((candidate) => candidate.url === '/api/v1/transactions');
    expect(request.request.params.get('toDate')).toBe('2026-11-11');
    request.flush({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0, totalPages: 0, hasNextPage: false });
  });

  it('shows server validation messages instead of offering a futile retry', () => {
    const fixture = render();
    respond();
    fixture.detectChanges();
    (fixture.nativeElement as HTMLElement).querySelector<HTMLFormElement>('form')!
      .dispatchEvent(new Event('submit'));
    http.expectOne((request) => request.url === '/api/v1/transactions').flush({
      errors: [{ pointer: '/toDate', code: 'date.invalidRange', message: 'toDate must not precede fromDate.' }],
    }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('toDate must not precede fromDate.');
    expect(fixture.nativeElement.querySelector('[data-testid="retry"]')).toBeNull();
  });

  it('does not offer network retry when authentication is required', () => {
    const fixture = render();
    http.expectOne((request) => request.url === '/api/v1/transactions')
      .flush({}, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Unable to access transactions');
    expect(fixture.nativeElement.querySelector('[data-testid="retry"]')).toBeNull();
  });

  it('sends filters and resets to page one', () => {
    const fixture = render();
    respond();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const type = element.querySelector<HTMLSelectElement>('[name="transactionType"]')!;
    const from = element.querySelector<HTMLInputElement>('[name="fromDate"]')!;
    const to = element.querySelector<HTMLInputElement>('[name="toDate"]')!;
    type.value = 'DebitOrder';
    type.dispatchEvent(new Event('change'));
    from.value = '2026-11-01';
    from.dispatchEvent(new Event('input'));
    to.value = '2026-11-10';
    to.dispatchEvent(new Event('input'));
    element.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    const request = http.expectOne((req) => req.url === '/api/v1/transactions');
    expect(request.request.params.get('transactionType')).toBe('DebitOrder');
    expect(request.request.params.get('fromDate')).toBe('2026-11-01');
    expect(request.request.params.get('toDate')).toBe('2026-11-10');
    expect(request.request.params.get('pageNumber')).toBe('1');
    request.flush({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0, totalPages: 0, hasNextPage: false });
  });
});
