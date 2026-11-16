import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AgentDisputesPage } from './agent-disputes-page';
import { AgentDisputeListItem, AgentDisputePage } from './agent-disputes-api';

describe('AgentDisputesPage', () => {
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
      imports: [AgentDisputesPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function render() {
    const fixture = TestBed.createComponent(AgentDisputesPage);
    fixture.detectChanges();
    return fixture;
  }

  function respond(page: AgentDisputePage = { items: [item], pageInfo: { hasNextPage: false } }) {
    const request = http.expectOne(candidate => candidate.url === '/api/v1/agent/disputes');
    expect(request.request.params.get('pageSize')).toBe('20');
    request.flush(page);
  }

  it('loads persisted cases and formats minor units without floating-point rounding', () => {
    const fixture = render();
    respond();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('DSP-AGENT000001');
    expect(fixture.nativeElement.textContent).toContain('ZAR 90,071,992,547,409.91');
    expect(fixture.nativeElement.textContent).toContain('CUS-7B2A31D940E2');
  });

  it('applies search and filters and continues with the opaque server cursor', () => {
    const fixture = render();
    respond({ items: [item], pageInfo: { hasNextPage: true, nextCursor: 'opaque-cursor' } });
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    root.querySelector<HTMLInputElement>('[name="search"]')!.value = ' DSP-AGENT ';
    root.querySelector<HTMLInputElement>('[name="search"]')!.dispatchEvent(new Event('input'));
    const category = root.querySelector<HTMLSelectElement>('[name="category"]')!;
    category.value = 'CardPurchase';
    category.dispatchEvent(new Event('change'));
    root.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
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
    expect(fixture.nativeElement.textContent).toContain('No matching disputes found');
  });

  it('shows a retry after a transient queue failure', () => {
    const fixture = render();
    const root = fixture.nativeElement as HTMLElement;
    http.expectOne(request => request.url === '/api/v1/agent/disputes')
      .flush({}, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Unable to load the Agent queue');
    root.querySelector<HTMLButtonElement>('[data-testid="retry"]')!.click();
    respond();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('DSP-AGENT000001');
  });
});
