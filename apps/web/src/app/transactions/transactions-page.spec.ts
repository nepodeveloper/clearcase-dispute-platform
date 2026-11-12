import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
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
      providers: [provideHttpClient(), provideHttpClientTesting()],
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
