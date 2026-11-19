import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AgentDisputeListItem, AgentDisputePage } from './agent-disputes-api';
import { AgentQueuePage } from './agent-queue-page';

describe('Agent queue', () => {
  const item: AgentDisputeListItem = {
    id: '00000000-0000-0000-0000-000000000001',
    reference: 'DSP-AGENT000001',
    customerReference: 'CUS-7B2A31D940E2',
    category: 'CardPurchase',
    transactionDescription: 'Synthetic card purchase',
    amountMinorUnits: '9007199254740991',
    currency: 'ZAR',
    reasonCode: 'unrecognisedTransaction',
    caseStatus: 'Submitted',
    decisionStatus: 'Pending',
    financialStatus: 'NotInitiated',
    createdAt: '2026-11-12T10:00:00Z',
    lastActivityAt: '2026-11-12T10:00:00Z',
    version: 1,
  };
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AgentQueuePage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.useRealTimers();
    http.verify();
  });

  function render() {
    const fixture = TestBed.createComponent(AgentQueuePage);
    fixture.detectChanges();
    return fixture;
  }

  function respond(page: AgentDisputePage = { items: [item], pageInfo: { hasNextPage: false } }) {
    const request = http.expectOne(candidate => candidate.url === '/api/v1/agent/disputes');
    expect(request.request.params.get('pageSize')).toBe('5');
    request.flush(page);
  }

  it('loads persisted cases and formats integer minor units without floating-point rounding', () => {
    const fixture = render();
    respond();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('DSP-AGENT000001');
    expect(fixture.nativeElement.textContent).toContain('R 90,071,992,547,409.91');
    expect(fixture.nativeElement.textContent).toContain('CUS-7B2A31D940E2');
  });

  it('applies search and filters and continues with the opaque server cursor', () => {
    const fixture = render();
    respond({ items: [item], pageInfo: { hasNextPage: true, nextCursor: 'opaque-cursor' } });
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const search = root.querySelector<HTMLInputElement>('input[type="search"]')!;
    search.value = ' DSP-AGENT ';
    search.dispatchEvent(new Event('input'));
    const category = root.querySelectorAll<HTMLSelectElement>('.operations-filters select')[1];
    category.value = 'CardPurchase';
    category.dispatchEvent(new Event('change'));
    root.querySelector<HTMLButtonElement>('.operations-filters button')!.click();

    const filtered = http.expectOne(request => request.url === '/api/v1/agent/disputes');
    expect(filtered.request.params.get('search')).toBe('DSP-AGENT');
    expect(filtered.request.params.get('category')).toBe('CardPurchase');
    expect(filtered.request.params.has('cursor')).toBe(false);
    filtered.flush({ items: [item], pageInfo: { hasNextPage: true, nextCursor: 'opaque-cursor' } });
    fixture.detectChanges();

    root.querySelector<HTMLButtonElement>('[data-testid="next"]')!.click();
    const next = http.expectOne(request => request.url === '/api/v1/agent/disputes');
    expect(next.request.params.get('cursor')).toBe('opaque-cursor');
    next.flush({ items: [], pageInfo: { hasNextPage: false } });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No matching cases');
  });

  it('restarts at page one with the chosen page size', () => {
    const fixture = render();
    respond({ items: [item], pageInfo: { hasNextPage: true, nextCursor: 'opaque-cursor' } });
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    root.querySelector<HTMLButtonElement>('[data-testid="next"]')!.click();
    http.expectOne(request => request.url === '/api/v1/agent/disputes')
      .flush({ items: [item], pageInfo: { hasNextPage: false } });
    fixture.detectChanges();

    const size = root.querySelector<HTMLSelectElement>('.page-size select')!;
    size.value = '50';
    size.dispatchEvent(new Event('change'));
    const resized = http.expectOne(request => request.url === '/api/v1/agent/disputes');
    expect(resized.request.params.get('pageSize')).toBe('50');
    expect(resized.request.params.has('cursor')).toBe(false);
    resized.flush({ items: [item], pageInfo: { hasNextPage: false } });
    fixture.detectChanges();
    expect(root.textContent).toContain('Page 1');
  });

  it('shows how long each case has been open and flags aging cases in text', () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-11-15T10:00:00Z'));
    const fixture = render();
    respond({
      items: [
        { ...item, id: 'fresh', reference: 'DSP-FRESH', createdAt: '2026-11-15T08:00:00Z' },
        { ...item, id: 'edge', reference: 'DSP-EDGE', createdAt: '2026-11-10T10:00:00Z' },
        { ...item, id: 'young', reference: 'DSP-YOUNG', createdAt: '2026-11-12T10:00:00Z' },
        { ...item, id: 'old', reference: 'DSP-OLD', createdAt: '2026-11-09T09:00:00Z' },
      ],
      pageInfo: { hasNextPage: false },
    });
    fixture.detectChanges();
    const rows = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr'));
    const row = (reference: string) => rows.find(r => r.textContent?.includes(reference))!;

    expect(row('DSP-FRESH').textContent).toContain('Opened today');
    expect(row('DSP-YOUNG').textContent).toContain('Open for 3 days');
    expect(row('DSP-YOUNG').querySelector('.tag-warning')).toBeNull();
    expect(row('DSP-EDGE').textContent).toContain('Open for 5 days');
    expect(row('DSP-EDGE').querySelector('.tag-warning')).toBeNull();
    expect(row('DSP-OLD').textContent).toContain('Open for 6 days');
    expect(row('DSP-OLD').querySelector('.tag-warning')?.textContent)
      .toContain('Aging');
    expect(row('DSP-OLD').querySelector('.tag-warning .cc-sr-only')?.textContent)
      .toContain('more than 5 days');
  });

  it('shows a retry after a transient queue failure', () => {
    const fixture = render();
    const root = fixture.nativeElement as HTMLElement;
    http.expectOne(request => request.url === '/api/v1/agent/disputes')
      .flush({}, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Unable to load persisted cases');
    [...root.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent?.includes('Retry'))!.click();
    respond();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('DSP-AGENT000001');
  });
});
