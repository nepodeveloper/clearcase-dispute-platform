import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AgentDisputeDetails, AgentDisputeListItem, AgentDisputePage } from './agent-disputes-api';
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
    slaDueAt: '2026-11-17T10:00:00Z',
    slaStatus: 'OnTrack',
  };
  let http: HttpTestingController;

  function details(row: AgentDisputeListItem): AgentDisputeDetails {
    return { id: row.id, reference: row.reference, slaDueAt: row.slaDueAt, slaStatus: row.slaStatus,
      financialOperations: [] } as unknown as AgentDisputeDetails;
  }

  function pressKey(key: string, init: KeyboardEventInit = {}, target: EventTarget = document) {
    target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init }));
  }

  async function nextRequest(method: string, url: string): Promise<TestRequest> {
    let request!: TestRequest;
    await vi.waitFor(() => { request = http.expectOne(candidate => candidate.method === method && candidate.url === url); });
    return request;
  }

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [AgentQueuePage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    localStorage.clear();
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

  it('applies dropdown choices immediately and continues with the opaque server cursor', () => {
    const fixture = render();
    respond({ items: [item], pageInfo: { hasNextPage: true, nextCursor: 'opaque-cursor' } });
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const category = root.querySelectorAll<HTMLSelectElement>('.operations-filters select')[1];
    category.value = 'CardPurchase';
    category.dispatchEvent(new Event('change'));

    const filtered = http.expectOne(request => request.url === '/api/v1/agent/disputes');
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

  describe('automatic filtering', () => {
    function type(root: HTMLElement, text: string) {
      const search = root.querySelector<HTMLInputElement>('input[type="search"]')!;
      search.value = text;
      search.dispatchEvent(new Event('input'));
      return search;
    }

    it('searches once after typing pauses and sends the trimmed text', () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
      const fixture = render();
      respond();
      const root = fixture.nativeElement as HTMLElement;
      type(root, 'D');
      vi.advanceTimersByTime(200);
      type(root, 'DSP');
      vi.advanceTimersByTime(200);
      type(root, ' DSP-AGENT ');
      vi.advanceTimersByTime(349);
      http.expectNone(request => request.url === '/api/v1/agent/disputes');

      vi.advanceTimersByTime(1);
      const filtered = http.expectOne(request => request.url === '/api/v1/agent/disputes');
      expect(filtered.request.params.get('search')).toBe('DSP-AGENT');
      filtered.flush({ items: [], pageInfo: { hasNextPage: false } });
    });

    it('does not refetch when typing ends on the filters already applied', () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
      const fixture = render();
      respond();
      const root = fixture.nativeElement as HTMLElement;
      type(root, 'abc');
      type(root, '  ');
      vi.advanceTimersByTime(1000);
      http.expectNone(request => request.url === '/api/v1/agent/disputes');
    });

    it('applies the typed search straight away on Enter and drops the pending pause', () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
      const fixture = render();
      respond();
      const search = type(fixture.nativeElement as HTMLElement, 'DSP-ENTER');
      search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
      const filtered = http.expectOne(request => request.url === '/api/v1/agent/disputes');
      expect(filtered.request.params.get('search')).toBe('DSP-ENTER');
      filtered.flush({ items: [], pageInfo: { hasNextPage: false } });
      vi.advanceTimersByTime(1000);
    });

    it('has no Search button because filters apply themselves', () => {
      const fixture = render();
      respond();
      expect((fixture.nativeElement as HTMLElement).querySelector('.operations-filters button')).toBeNull();
      expect(fixture.nativeElement.textContent).toContain('Results update as you type');
    });
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

  it('shows server SLA status as text badges with a due time, and none once complete', () => {
    const fixture = render();
    respond({
      items: [
        { ...item, id: 'a', reference: 'DSP-ON', slaStatus: 'OnTrack' },
        { ...item, id: 'b', reference: 'DSP-SOON', slaStatus: 'DueSoon', slaDueAt: '2026-11-13T09:30:00Z' },
        { ...item, id: 'c', reference: 'DSP-LATE', slaStatus: 'Breached' },
        { ...item, id: 'd', reference: 'DSP-DONE', slaStatus: 'Complete', caseStatus: 'Closed' },
      ],
      pageInfo: { hasNextPage: false },
    });
    fixture.detectChanges();
    const rows = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr'));
    const sla = (reference: string) => rows.find(r => r.textContent?.includes(reference))!
      .querySelector('[data-testid="sla-cell"]')!;

    expect(sla('DSP-ON').textContent).toContain('On track');
    expect(sla('DSP-SOON').textContent).toContain('Due soon');
    expect(sla('DSP-SOON').textContent).toContain('13 Nov, 09:30 UTC');
    expect(sla('DSP-LATE').textContent).toContain('Breached');
    expect(sla('DSP-LATE').querySelector('.cc-status--error')).not.toBeNull();
    expect(sla('DSP-DONE').querySelector('.cc-status')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Aging');
  });

  it('filters by assignment, toggles Mine with the m key and shows the assignee', () => {
    const fixture = render();
    respond({
      items: [{ ...item, assignedTo: { employeeId: 'emp-1', displayName: 'Thandi Agent' } }, { ...item, id: 'free', reference: 'DSP-FREE' }],
      pageInfo: { hasNextPage: false },
    });
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const assignees = Array.from(root.querySelectorAll('[data-testid="assignee-cell"]'), cell => cell.textContent?.trim());
    expect(assignees).toEqual(['Thandi Agent', 'Unassigned']);

    const select = root.querySelector<HTMLSelectElement>('[data-testid="assignment-filter"]')!;
    select.value = 'unassigned';
    select.dispatchEvent(new Event('change'));
    const unassigned = http.expectOne(request => request.url === '/api/v1/agent/disputes');
    expect(unassigned.request.params.get('assignment')).toBe('unassigned');
    unassigned.flush({ items: [], pageInfo: { hasNextPage: false } });
    fixture.detectChanges();

    pressKey('m');
    const mine = http.expectOne(request => request.url === '/api/v1/agent/disputes');
    expect(mine.request.params.get('assignment')).toBe('mine');
    mine.flush({ items: [item], pageInfo: { hasNextPage: false } });
    fixture.detectChanges();
    expect(select.value).toBe('mine');

    pressKey('m');
    const all = http.expectOne(request => request.url === '/api/v1/agent/disputes');
    expect(all.request.params.has('assignment')).toBe(false);
    all.flush({ items: [item], pageInfo: { hasNextPage: false } });
  });

  it('claims and releases a case from its row without touching case state', async () => {
    const fixture = render();
    respond();
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const assigned = { ...details(item), assignedTo: { employeeId: 'emp-1', displayName: 'Thandi Agent' } };

    root.querySelector<HTMLButtonElement>('[data-testid="claim"]')!.click();
    (await nextRequest('POST', `/api/v1/agent/disputes/${item.id}/assignment`)).flush(assigned);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="assignee-cell"]')?.textContent).toContain('Thandi Agent');
    expect(root.querySelector('[data-testid="queue-status"]')?.textContent).toContain('Claimed DSP-AGENT000001.');
    expect(root.querySelector('[data-testid="claim"]')).toBeNull();

    root.querySelector<HTMLButtonElement>('[data-testid="release"]')!.click();
    (await nextRequest('DELETE', `/api/v1/agent/disputes/${item.id}/assignment`)).flush(details(item));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="assignee-cell"]')?.textContent).toContain('Unassigned');
    expect(root.querySelector('[data-testid="queue-status"]')?.textContent).toContain('Released DSP-AGENT000001.');
  });

  it('explains a refused claim and keeps the row unchanged', async () => {
    const fixture = render();
    respond();
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    root.querySelector<HTMLButtonElement>('[data-testid="claim"]')!.click();
    (await nextRequest('POST', `/api/v1/agent/disputes/${item.id}/assignment`))
      .flush({ code: 'dispute.alreadyAssigned' }, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="queue-status"]')?.textContent)
      .toContain('Could not claim DSP-AGENT000001: already assigned to another employee.');
    expect(root.querySelector('[data-testid="claim"]')).not.toBeNull();
  });

  it('assigns the selected rows to me one claim at a time and reports each failure', async () => {
    const second = { ...item, id: '00000000-0000-0000-0000-000000000002', reference: 'DSP-AGENT000002' };
    const third = { ...item, id: '00000000-0000-0000-0000-000000000003', reference: 'DSP-AGENT000003' };
    const fixture = render();
    respond({ items: [item, second, third], pageInfo: { hasNextPage: false } });
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const bulk = root.querySelector<HTMLButtonElement>('[data-testid="bulk-claim"]')!;
    expect(bulk.disabled).toBe(true);
    expect(root.querySelector('[data-testid="select-all"]')?.getAttribute('aria-label')).toBe('Select all cases on this page');
    expect(root.querySelector('[data-testid="row-select"]')?.getAttribute('aria-label')).toBe('Select DSP-AGENT000001');

    const all = root.querySelector<HTMLInputElement>('[data-testid="select-all"]')!;
    all.checked = true;
    all.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(bulk.disabled).toBe(false);
    expect(root.textContent).toContain('3 selected');

    bulk.click();
    const mineNow = { employeeId: 'emp-1', displayName: 'Thandi Agent' };
    (await nextRequest('POST', `/api/v1/agent/disputes/${item.id}/assignment`)).flush({ ...details(item), assignedTo: mineNow });
    (await nextRequest('POST', `/api/v1/agent/disputes/${second.id}/assignment`))
      .flush({ code: 'dispute.alreadyAssigned' }, { status: 409, statusText: 'Conflict' });
    (await nextRequest('POST', `/api/v1/agent/disputes/${third.id}/assignment`)).flush({ ...details(third), assignedTo: mineNow });
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(bulk.textContent).toContain('Assign selected to me');
    });

    const status = root.querySelector('[data-testid="queue-status"]')!;
    expect(status.getAttribute('aria-live')).toBe('polite');
    expect(status.textContent).toContain('Assigned 2 of 3 selected cases to you.');
    expect(status.textContent).toContain('DSP-AGENT000002: already assigned to another employee');
    expect(root.textContent).toContain('0 selected');
    http.expectNone(request => request.url.endsWith('/commands'));
  });

  it('saves, applies and deletes named filters in localStorage and ignores malformed stored data', () => {
    localStorage.setItem('clearcase.queue.filters', JSON.stringify([{ name: 'Bad', search: 1 }, { name: 'Old card', search: 'refund',
      category: 'CardPurchase', status: 'Submitted', assignment: 'mine' }]));
    const fixture = render();
    respond();
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const options = () => Array.from(root.querySelectorAll<HTMLOptionElement>('[data-testid="preset-select"] option'), o => o.textContent?.trim());
    expect(options()).toEqual(['Choose a saved filter', 'Old card']);

    const search = root.querySelector<HTMLInputElement>('input[type="search"]')!;
    search.value = 'chargeback';
    search.dispatchEvent(new Event('input'));
    const name = root.querySelector<HTMLInputElement>('[data-testid="preset-name"]')!;
    name.value = 'Chargebacks';
    name.dispatchEvent(new Event('input'));
    root.querySelector<HTMLButtonElement>('[data-testid="preset-save"]')!.click();
    fixture.detectChanges();
    expect(options()).toEqual(['Choose a saved filter', 'Old card', 'Chargebacks']);
    expect(JSON.parse(localStorage.getItem('clearcase.queue.filters')!)).toHaveLength(2);
    expect(JSON.parse(localStorage.getItem('clearcase.queue.filters')!)[1]).toEqual({
      name: 'Chargebacks', search: 'chargeback', category: '', status: '', assignment: 'all' });

    const select = root.querySelector<HTMLSelectElement>('[data-testid="preset-select"]')!;
    select.value = 'Old card';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    root.querySelector<HTMLButtonElement>('[data-testid="preset-apply"]')!.click();
    const applied = http.expectOne(request => request.url === '/api/v1/agent/disputes');
    expect(applied.request.params.get('search')).toBe('refund');
    expect(applied.request.params.get('category')).toBe('CardPurchase');
    expect(applied.request.params.get('caseStatus')).toBe('Submitted');
    expect(applied.request.params.get('assignment')).toBe('mine');
    applied.flush({ items: [item], pageInfo: { hasNextPage: false } });
    fixture.detectChanges();
    expect(root.querySelector<HTMLInputElement>('input[type="search"]')!.value).toBe('refund');

    root.querySelector<HTMLButtonElement>('[data-testid="preset-delete"]')!.click();
    fixture.detectChanges();
    expect(options()).toEqual(['Choose a saved filter', 'Chargebacks']);
    expect(JSON.parse(localStorage.getItem('clearcase.queue.filters')!)).toHaveLength(1);
  });

  it('still shows saved filters for the visit when storage is blocked', () => {
    const fixture = render();
    respond();
    fixture.detectChanges();
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new DOMException('blocked'); });
    const root = fixture.nativeElement as HTMLElement;
    const name = root.querySelector<HTMLInputElement>('[data-testid="preset-name"]')!;
    name.value = 'Temp';
    name.dispatchEvent(new Event('input'));
    root.querySelector<HTMLButtonElement>('[data-testid="preset-save"]')!.click();
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="preset-message"]')?.textContent).toContain('this browser blocked saving');
    vi.restoreAllMocks();
  });

  it('supports / j k Enter shortcuts and ignores them while typing or with a modifier key', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const second = { ...item, id: '00000000-0000-0000-0000-000000000002', reference: 'DSP-AGENT000002' };
    const fixture = render();
    respond({ items: [item, second], pageInfo: { hasNextPage: false } });
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const search = root.querySelector<HTMLInputElement>('input[type="search"]')!;
    const links = root.querySelectorAll<HTMLAnchorElement>('a.case-link');

    pressKey('/');
    expect(document.activeElement).toBe(search);
    pressKey('j', {}, search);
    expect(document.activeElement).toBe(search);
    pressKey('m', {}, search);
    http.expectNone(request => request.url === '/api/v1/agent/disputes');

    search.blur();
    pressKey('j', { ctrlKey: true });
    pressKey('j', { altKey: true });
    pressKey('j', { metaKey: true });
    expect(document.activeElement).not.toBe(links[0]);

    pressKey('j');
    expect(document.activeElement).toBe(links[0]);
    pressKey('j');
    expect(document.activeElement).toBe(links[1]);
    pressKey('j');
    expect(document.activeElement).toBe(links[1]);
    pressKey('k');
    expect(document.activeElement).toBe(links[0]);
    fixture.detectChanges();
    expect(root.querySelector('tbody tr.is-active')?.textContent).toContain('DSP-AGENT000001');

    pressKey('Enter', {}, links[0]);
    expect(navigate).not.toHaveBeenCalled();
    (document.activeElement as HTMLElement).blur();
    pressKey('Enter');
    expect(navigate).toHaveBeenCalledWith(['/agent/disputes', item.id]);
  });

  it('lists the keyboard shortcuts in a visible disclosure', () => {
    const fixture = render();
    respond();
    fixture.detectChanges();
    const help = (fixture.nativeElement as HTMLElement).querySelector('.shortcut-help')!;
    expect(help.querySelector('summary')?.textContent).toContain('Keyboard shortcuts');
    expect(Array.from(help.querySelectorAll('kbd'), key => key.textContent)).toEqual(['/', 'j', 'k', 'Enter', 'm']);
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
