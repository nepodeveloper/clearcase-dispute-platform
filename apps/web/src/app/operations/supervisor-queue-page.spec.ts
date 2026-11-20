import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SupervisorEscalationListItem } from './supervisor-escalations-api';
import { SupervisorQueuePage } from './supervisor-queue-page';

describe('Supervisor queue', () => {
  const item: SupervisorEscalationListItem = {
    id: '00000000-0000-0000-0000-000000000010',
    reference: 'ESC-00000000000000000000000000000010',
    disputeId: '00000000-0000-0000-0000-000000000011',
    disputeReference: 'DSP-SUPERVISOR-CASE',
    customerReference: 'CUS-7B2A31D940E2',
    reason: 'Policy exception review',
    status: 'Open',
    createdAt: '2026-11-12T10:00:00Z',
  };
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [SupervisorQueuePage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.useRealTimers();
    http.verify();
  });

  function render() {
    const fixture = TestBed.createComponent(SupervisorQueuePage);
    fixture.detectChanges();
    return fixture;
  }

  it('loads persisted escalations and uses status, search, and opaque cursor filters', () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    const fixture = render();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector<HTMLSelectElement>('select')!.value).toBe('');
    const initial = http.expectOne(request => request.url === '/api/v1/supervisor/escalations');
    expect(initial.request.params.get('pageSize')).toBe('5');
    expect(initial.request.params.has('status')).toBe(false);
    initial.flush({ items: [item], pageInfo: { hasNextPage: true, nextCursor: 'opaque-cursor' } });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain(item.reference);
    expect(fixture.nativeElement.textContent).toContain(item.disputeReference);

    const search = root.querySelector<HTMLInputElement>('input[type="search"]')!;
    search.value = ' DSP-SUPERVISOR ';
    search.dispatchEvent(new Event('input'));
    http.expectNone(request => request.url === '/api/v1/supervisor/escalations');
    vi.advanceTimersByTime(350);
    const filtered = http.expectOne(request => request.url === '/api/v1/supervisor/escalations');
    expect(filtered.request.params.get('search')).toBe('DSP-SUPERVISOR');
    expect(filtered.request.params.has('status')).toBe(false);
    filtered.flush({ items: [item], pageInfo: { hasNextPage: true, nextCursor: 'filtered-cursor' } });
    fixture.detectChanges();

    const status = root.querySelector<HTMLSelectElement>('select')!;
    status.value = 'Acknowledged';
    status.dispatchEvent(new Event('change'));
    const byStatus = http.expectOne(request => request.url === '/api/v1/supervisor/escalations');
    expect(byStatus.request.params.get('status')).toBe('Acknowledged');
    expect(byStatus.request.params.has('cursor')).toBe(false);
    byStatus.flush({ items: [item], pageInfo: { hasNextPage: true, nextCursor: 'status-cursor' } });
    fixture.detectChanges();

    root.querySelector<HTMLButtonElement>('.queue-pagination button:last-child')!.click();
    const next = http.expectOne(request => request.url === '/api/v1/supervisor/escalations');
    expect(next.request.params.get('cursor')).toBe('status-cursor');
    next.flush({ items: [], pageInfo: { hasNextPage: false } });
    fixture.detectChanges();
    expect(root.textContent).toContain('No matching escalations');

    const size = root.querySelector<HTMLSelectElement>('.page-size select')!;
    size.value = '20';
    size.dispatchEvent(new Event('change'));
    const resized = http.expectOne(request => request.url === '/api/v1/supervisor/escalations');
    expect(resized.request.params.get('pageSize')).toBe('20');
    expect(resized.request.params.get('status')).toBe('Acknowledged');
    expect(resized.request.params.has('cursor')).toBe(false);
    resized.flush({ items: [item], pageInfo: { hasNextPage: false } });
    fixture.detectChanges();
    expect(root.textContent).toContain('Page 1');
  });

  it('collapses rapid typing into one search, skips unchanged filters and applies Enter immediately', () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    const fixture = render();
    const root = fixture.nativeElement as HTMLElement;
    http.expectOne(request => request.url === '/api/v1/supervisor/escalations')
      .flush({ items: [], pageInfo: { hasNextPage: false } });
    const search = root.querySelector<HTMLInputElement>('input[type="search"]')!;
    const type = (text: string) => { search.value = text; search.dispatchEvent(new Event('input')); };

    type('E'); vi.advanceTimersByTime(200);
    type('ESC'); vi.advanceTimersByTime(200);
    type('ESC-1'); vi.advanceTimersByTime(349);
    http.expectNone(request => request.url === '/api/v1/supervisor/escalations');
    vi.advanceTimersByTime(1);
    const typed = http.expectOne(request => request.url === '/api/v1/supervisor/escalations');
    expect(typed.request.params.get('search')).toBe('ESC-1');
    typed.flush({ items: [], pageInfo: { hasNextPage: false } });

    type('ESC-1  ');
    vi.advanceTimersByTime(1000);
    http.expectNone(request => request.url === '/api/v1/supervisor/escalations');

    type('ESC-2');
    search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    const entered = http.expectOne(request => request.url === '/api/v1/supervisor/escalations');
    expect(entered.request.params.get('search')).toBe('ESC-2');
    entered.flush({ items: [], pageInfo: { hasNextPage: false } });
    vi.advanceTimersByTime(1000);
  });
});
