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

  function respond(items = [item], nextCursor?: string, totalCount: number | undefined = items.length) {
    http.expectOne((request) => request.url === '/api/v1/transactions').flush({
      items, pageInfo: { hasNextPage: nextCursor !== undefined, nextCursor }, totalCount,
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
      pageInfo: { hasNextPage: false }, totalCount: 2,
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

  it('loads more rows with the opaque cursor, appends them, announces the count and keeps focus', async () => {
    const fixture = render();
    const initial = http.expectOne((req) => req.url === '/api/v1/transactions');
    expect(initial.request.params.get('pageNumber')).toBe('1');
    expect(initial.request.params.has('cursor')).toBe(false);
    initial.flush({ items: [item], pageInfo: { hasNextPage: true, nextCursor: 'cursor-2' }, totalCount: 2 });
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const status = element.querySelector<HTMLElement>('[data-testid="transactions-status"]')!;
    expect(status.getAttribute('role')).toBe('status');
    expect(status.textContent).toContain('Showing 1 of 2 transactions');
    expect(element.querySelector('[data-testid="next"]')).toBeNull();

    const more = element.querySelector<HTMLButtonElement>('[data-testid="load-more"]')!;
    more.focus();
    more.click();
    fixture.detectChanges();
    expect(more.getAttribute('aria-disabled')).toBe('true');
    more.click();
    const request = http.expectOne((req) => req.url === '/api/v1/transactions');
    expect(request.request.params.get('cursor')).toBe('cursor-2');
    expect(request.request.params.has('pageNumber')).toBe(false);
    expect(request.request.params.get('pageSize')).toBe('5');
    request.flush({ items: [{ ...item, id: 'second', description: 'Checkers' }], pageInfo: { hasNextPage: false }, totalCount: 2 });
    fixture.detectChanges();
    await fixture.whenStable();

    expect(Array.from(element.querySelectorAll('tbody tr'), row => row.textContent)).toEqual([
      expect.stringContaining('Woolworths'), expect.stringContaining('Checkers')]);
    expect(status.textContent).toContain('Showing 2 of 2 transactions');
    expect(element.querySelector('[data-testid="load-more"]')).toBeNull();
    expect(element.textContent).toContain('All matching transactions are shown.');
    expect(document.activeElement).toBe(status);
  });

  it('counts loaded rows when the server sends no total', () => {
    const fixture = render();
    http.expectOne((req) => req.url === '/api/v1/transactions')
      .flush({ items: [item], pageInfo: { hasNextPage: true, nextCursor: 'cursor-2' } });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="transactions-status"]').textContent)
      .toContain('Showing 1+ transactions');
  });

  it('keeps loaded rows when loading more fails and retries the same cursor', () => {
    const fixture = render();
    respond([item], 'cursor-2', 2);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    element.querySelector<HTMLButtonElement>('[data-testid="load-more"]')!.click();
    http.expectOne((req) => req.url === '/api/v1/transactions').flush('error', { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="more-error"]')?.getAttribute('role')).toBe('alert');
    expect(element.textContent).toContain('Woolworths');

    element.querySelector<HTMLButtonElement>('[data-testid="retry-more"]')!.click();
    const retry = http.expectOne((req) => req.url === '/api/v1/transactions');
    expect(retry.request.params.get('cursor')).toBe('cursor-2');
    retry.flush({ items: [{ ...item, id: 'second', description: 'Checkers' }], pageInfo: { hasNextPage: false }, totalCount: 2 });
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="more-error"]')).toBeNull();
    expect(element.querySelectorAll('tbody tr')).toHaveLength(2);
  });

  it('offers a reload instead of a futile retry when the cursor is rejected', () => {
    const fixture = render();
    respond([item], 'stale', 2);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    element.querySelector<HTMLButtonElement>('[data-testid="load-more"]')!.click();
    http.expectOne((req) => req.url === '/api/v1/transactions')
      .flush({ errors: [{ pointer: '/cursor', code: 'cursor.invalid', message: 'Invalid cursor.' }] }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="retry-more"]')).toBeNull();
    element.querySelector<HTMLButtonElement>('[data-testid="reload"]')!.click();
    const reload = http.expectOne((req) => req.url === '/api/v1/transactions');
    expect(reload.request.params.has('cursor')).toBe(false);
    expect(reload.request.params.get('pageNumber')).toBe('1');
    reload.flush({ items: [item], pageInfo: { hasNextPage: false }, totalCount: 1 });
  });

  it('restarts from the first page with the applied filters replacing loaded rows', () => {
    const fixture = render();
    respond([item], 'cursor-2', 2);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    element.querySelector<HTMLButtonElement>('[data-testid="load-more"]')!.click();
    http.expectOne((req) => req.url === '/api/v1/transactions')
      .flush({ items: [{ ...item, id: 'second', description: 'Checkers' }], pageInfo: { hasNextPage: true, nextCursor: 'cursor-3' }, totalCount: 3 });
    fixture.detectChanges();
    const search = element.querySelector<HTMLInputElement>('[name="search"]')!;
    search.value = 'Woolworths';
    search.dispatchEvent(new Event('input'));
    element.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    const request = http.expectOne((req) => req.url === '/api/v1/transactions');
    expect(request.request.params.has('cursor')).toBe(false);
    expect(request.request.params.get('pageNumber')).toBe('1');
    expect(request.request.params.get('search')).toBe('Woolworths');
    request.flush({ items: [item], pageInfo: { hasNextPage: true, nextCursor: 'filtered-2' }, totalCount: 2 });
    fixture.detectChanges();
    expect(element.querySelectorAll('tbody tr')).toHaveLength(1);

    element.querySelector<HTMLButtonElement>('[data-testid="load-more"]')!.click();
    const next = http.expectOne((req) => req.url === '/api/v1/transactions');
    expect(next.request.params.get('cursor')).toBe('filtered-2');
    expect(next.request.params.get('search')).toBe('Woolworths');
    next.flush({ items: [], pageInfo: { hasNextPage: false }, totalCount: 2 });
  });

  it('offers 5, 10, 20 and 50 rows per page and restarts with focus on the heading when the size changes', () => {
    const fixture = render();
    const initial = http.expectOne((request) => request.url === '/api/v1/transactions');
    expect(initial.request.params.get('pageSize')).toBe('5');
    initial.flush({ items: [item], pageInfo: { hasNextPage: true, nextCursor: 'cursor-2' }, totalCount: 6 });
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    element.querySelector<HTMLButtonElement>('[data-testid="load-more"]')!.click();
    http.expectOne((request) => request.url === '/api/v1/transactions')
      .flush({ items: [{ ...item, id: 'second' }], pageInfo: { hasNextPage: true, nextCursor: 'cursor-3' }, totalCount: 6 });
    fixture.detectChanges();

    const size = element.querySelector<HTMLSelectElement>('.page-size select')!;
    expect([...size.options].map(option => option.value)).toEqual(['5', '10', '20', '50']);
    expect(size.value).toBe('5');
    size.focus();
    size.value = '10';
    size.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(element.querySelector('.page-size select')).not.toBeNull();
    expect(element.querySelector('section')!.getAttribute('aria-busy')).toBe('true');
    const resized = http.expectOne((request) => request.url === '/api/v1/transactions');
    expect(resized.request.params.get('pageSize')).toBe('10');
    expect(resized.request.params.get('pageNumber')).toBe('1');
    expect(resized.request.params.has('cursor')).toBe(false);
    resized.flush({ items: [item], pageInfo: { hasNextPage: false }, totalCount: 6 });
    fixture.detectChanges();
    expect(element.querySelector<HTMLSelectElement>('.page-size select')!.value).toBe('10');
    expect(element.querySelectorAll('tbody tr')).toHaveLength(1);
    expect(document.activeElement).toBe(element.querySelector('#transactions-heading'));
  });

  it('explains an invalid date range without sending it and allows correction', () => {
    const fixture = render();
    respond();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const from = element.querySelector<HTMLInputElement>('[name="fromDate"]')!;
    const to = element.querySelector<HTMLInputElement>('[name="toDate"]')!;
    from.value = '2026-11-10';
    from.dispatchEvent(new Event('input'));
    http.expectOne((request) => request.url === '/api/v1/transactions')
      .flush({ items: [], pageInfo: { hasNextPage: false }, totalCount: 0 });

    to.value = '2026-11-09';
    to.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    http.expectNone((request) => request.url === '/api/v1/transactions');
    expect(element.textContent).toContain('To date must not precede from date.');
    expect(to.getAttribute('aria-invalid')).toBe('true');
    expect(to.getAttribute('aria-describedby')).toBe('date-error');
    expect(element.querySelector('[data-testid="retry"]')).toBeNull();

    to.value = '2026-11-11';
    to.dispatchEvent(new Event('input'));
    const request = http.expectOne((candidate) => candidate.url === '/api/v1/transactions');
    expect(request.request.params.get('toDate')).toBe('2026-11-11');
    request.flush({ items: [], pageInfo: { hasNextPage: false }, totalCount: 0 });
  });

  it('shows filter dates as dd MMM yyyy, sends ISO dates and stays quiet about half-typed dates', () => {
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
    fixture.detectChanges();
    http.expectNone((request) => request.url === '/api/v1/transactions');
    expect(element.textContent).not.toContain('Enter dates as dd MMM yyyy');
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    http.expectNone((request) => request.url === '/api/v1/transactions');
    expect(element.textContent).toContain('Enter dates as dd MMM yyyy, for example 01 Nov 2026.');
    expect(from.getAttribute('aria-invalid')).toBe('true');

    from.value = '1 nov 2026';
    from.dispatchEvent(new Event('input'));
    from.dispatchEvent(new Event('blur'));
    const fromRequest = http.expectOne((candidate) => candidate.url === '/api/v1/transactions');
    expect(fromRequest.request.params.get('fromDate')).toBe('2026-11-01');
    fromRequest.flush({ items: [], pageInfo: { hasNextPage: false }, totalCount: 0 });
    const picker = element.querySelectorAll<HTMLInputElement>('cc-date-field input[type="date"]')[1];
    picker.value = '2026-11-10';
    picker.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(from.value).toBe('01 Nov 2026');
    expect(to.value).toBe('10 Nov 2026');
    expect(from.getAttribute('aria-invalid')).toBeNull();

    const request = http.expectOne((candidate) => candidate.url === '/api/v1/transactions');
    expect(request.request.params.get('fromDate')).toBe('2026-11-01');
    expect(request.request.params.get('toDate')).toBe('2026-11-10');
    request.flush({ items: [], pageInfo: { hasNextPage: false }, totalCount: 0 });
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

  it('applies the type and dates immediately, with the trimmed search, and resets to page one', () => {
    const fixture = render();
    respond();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const type = element.querySelector<HTMLSelectElement>('[name="transactionType"]')!;
    const from = element.querySelector<HTMLInputElement>('[name="fromDate"]')!;
    const to = element.querySelector<HTMLInputElement>('[name="toDate"]')!;
    const search = element.querySelector<HTMLInputElement>('[name="search"]')!;
    const nextRequest = () => http.expectOne((req) => req.url === '/api/v1/transactions');
    const empty = { items: [], pageInfo: { hasNextPage: false }, totalCount: 0 };

    search.value = '  Woolworths  ';
    search.dispatchEvent(new Event('input'));
    type.value = 'DebitOrder';
    type.dispatchEvent(new Event('change'));
    const byType = nextRequest();
    expect(byType.request.params.get('transactionType')).toBe('DebitOrder');
    expect(byType.request.params.get('search')).toBe('Woolworths');
    byType.flush(empty);
    from.value = '2026-11-01';
    from.dispatchEvent(new Event('input'));
    nextRequest().flush(empty);
    to.value = '2026-11-10';
    to.dispatchEvent(new Event('input'));
    const request = nextRequest();
    expect(request.request.params.get('transactionType')).toBe('DebitOrder');
    expect(request.request.params.get('fromDate')).toBe('2026-11-01');
    expect(request.request.params.get('toDate')).toBe('2026-11-10');
    expect(request.request.params.get('pageNumber')).toBe('1');
    expect(request.request.params.get('search')).toBe('Woolworths');
    request.flush(empty);
  });

  describe('automatic filtering', () => {
    const typeSearch = (element: HTMLElement, text: string) => {
      const search = element.querySelector<HTMLInputElement>('[name="search"]')!;
      search.value = text;
      search.dispatchEvent(new Event('input'));
      return search;
    };

    afterEach(() => vi.useRealTimers());

    it('searches once after typing pauses instead of on every keystroke', () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
      const fixture = render();
      respond();
      const element = fixture.nativeElement as HTMLElement;
      typeSearch(element, 'W');
      vi.advanceTimersByTime(200);
      typeSearch(element, 'Wool');
      vi.advanceTimersByTime(200);
      typeSearch(element, ' Woolworths ');
      vi.advanceTimersByTime(349);
      http.expectNone((req) => req.url === '/api/v1/transactions');

      vi.advanceTimersByTime(1);
      const request = http.expectOne((req) => req.url === '/api/v1/transactions');
      expect(request.request.params.get('search')).toBe('Woolworths');
      request.flush({ items: [], pageInfo: { hasNextPage: false }, totalCount: 0 });
    });

    it('does not refetch when typing ends on the filters already applied', () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
      const fixture = render();
      respond();
      const element = fixture.nativeElement as HTMLElement;
      typeSearch(element, 'abc');
      typeSearch(element, '   ');
      vi.advanceTimersByTime(1000);
      http.expectNone((req) => req.url === '/api/v1/transactions');
    });

    it('applies on Enter without waiting and drops the pending pause', () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
      const fixture = render();
      respond();
      const search = typeSearch(fixture.nativeElement as HTMLElement, 'ATM');
      search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
      const request = http.expectOne((req) => req.url === '/api/v1/transactions');
      expect(request.request.params.get('search')).toBe('ATM');
      request.flush({ items: [], pageInfo: { hasNextPage: false }, totalCount: 0 });
      vi.advanceTimersByTime(1000);
    });

    it('has no Apply button because filters apply themselves', () => {
      const fixture = render();
      respond();
      const element = fixture.nativeElement as HTMLElement;
      expect(element.querySelector('form button[type="submit"]')).toBeNull();
      expect(element.textContent).not.toContain('Apply filters');
      expect(element.textContent).toContain('Results update as you type');
    });
  });
});
