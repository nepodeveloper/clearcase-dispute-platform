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

  it('links an existing dispute and offers only details for a pending transaction', () => {
    const fixture = render();
    const disputeId = 'aaaaaaaa-1111-4222-8333-444455556666';
    http.expectOne(request => request.url === '/api/v1/transactions').flush({
      items: [
        { ...item, activeDisputeId: disputeId },
        { ...item, id: '00000000-0000-0000-0000-000000000004', status: 'PendingAuthorization', isDisputable: false },
      ],
      pageNumber: 1, pageSize: 20, totalCount: 2, totalPages: 1, hasNextPage: false,
    });
    fixture.detectChanges();
    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr');
    expect(rows[0].querySelector(`a[href="/disputes/${disputeId}"]`)?.textContent).toContain('View dispute');
    expect(rows[0].textContent).toContain('Dispute in progress');
    expect(rows[1].querySelector('.row-action')?.textContent).toContain('View details');
    expect(rows[1].textContent).not.toContain('View & dispute');
    expect(rows[1].textContent).toContain('Pending card payment');
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

  it('offers 5, 10, 20 and 50 rows per page and restarts at page one when the size changes', () => {
    const fixture = render();
    const initial = http.expectOne((request) => request.url === '/api/v1/transactions');
    expect(initial.request.params.get('pageSize')).toBe('5');
    initial.flush({ items: [item], pageNumber: 1, pageSize: 5, totalCount: 6, totalPages: 2, hasNextPage: true });
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    element.querySelector<HTMLButtonElement>('[data-testid="next"]')!.click();
    http.expectOne((request) => request.url === '/api/v1/transactions')
      .flush({ items: [item], pageNumber: 2, pageSize: 5, totalCount: 6, totalPages: 2, hasNextPage: false });
    fixture.detectChanges();

    const size = element.querySelector<HTMLSelectElement>('.page-size select')!;
    expect([...size.options].map(option => option.value)).toEqual(['5', '10', '20', '50']);
    expect(size.value).toBe('5');
    size.value = '10';
    size.dispatchEvent(new Event('change'));
    const resized = http.expectOne((request) => request.url === '/api/v1/transactions');
    expect(resized.request.params.get('pageSize')).toBe('10');
    expect(resized.request.params.get('pageNumber')).toBe('1');
    resized.flush({ items: [item], pageNumber: 1, pageSize: 10, totalCount: 6, totalPages: 1, hasNextPage: false });
    fixture.detectChanges();
    expect(element.querySelector<HTMLSelectElement>('.page-size select')!.value).toBe('10');
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
    expect(to.getAttribute('aria-describedby')).toBe('date-error');
    expect(element.querySelector('[data-testid="retry"]')).toBeNull();

    to.value = '2026-11-11';
    to.dispatchEvent(new Event('input'));
    form.dispatchEvent(new Event('submit'));
    const request = http.expectOne((candidate) => candidate.url === '/api/v1/transactions');
    expect(request.request.params.get('toDate')).toBe('2026-11-11');
    request.flush({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0, totalPages: 0, hasNextPage: false });
  });

  it('shows filter dates as dd MMM yyyy, sends ISO dates and rejects unreadable dates', () => {
    const fixture = render();
    respond();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const from = element.querySelector<HTMLInputElement>('[name="fromDate"]')!;
    const to = element.querySelector<HTMLInputElement>('[name="toDate"]')!;
    const form = element.querySelector<HTMLFormElement>('form')!;
    expect(from.placeholder).toBe('dd MMM yyyy');

    from.value = '31 Feb 2026';
    from.dispatchEvent(new Event('input'));
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    http.expectNone((request) => request.url === '/api/v1/transactions');
    expect(element.textContent).toContain('Enter dates as dd MMM yyyy, for example 01 Nov 2026.');
    expect(from.getAttribute('aria-invalid')).toBe('true');

    from.value = '1 nov 2026';
    from.dispatchEvent(new Event('input'));
    from.dispatchEvent(new Event('blur'));
    const picker = element.querySelectorAll<HTMLInputElement>('cc-date-field input[type="date"]')[1];
    picker.value = '2026-11-10';
    picker.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(from.value).toBe('01 Nov 2026');
    expect(to.value).toBe('10 Nov 2026');
    expect(from.getAttribute('aria-invalid')).toBeNull();

    form.dispatchEvent(new Event('submit'));
    const request = http.expectOne((candidate) => candidate.url === '/api/v1/transactions');
    expect(request.request.params.get('fromDate')).toBe('2026-11-01');
    expect(request.request.params.get('toDate')).toBe('2026-11-10');
    request.flush({ items: [], pageNumber: 1, pageSize: 5, totalCount: 0, totalPages: 0, hasNextPage: false });
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
    const search = element.querySelector<HTMLInputElement>('[name="search"]')!;
    search.value = '  Woolworths  ';
    search.dispatchEvent(new Event('input'));
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
    expect(request.request.params.get('search')).toBe('Woolworths');
    request.flush({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0, totalPages: 0, hasNextPage: false });
  });
});
