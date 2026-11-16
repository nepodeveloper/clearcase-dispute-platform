import { inject } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AgentDisputesApi } from '../agent/agent-disputes-api';
import { OPERATIONS_API } from '../operations/operations-api';
import { DemoSupervisorQueuePage } from '../operations/supervisor-queue-page';
import { SupervisorEscalationListItem, SupervisorEscalationsApi } from './supervisor-escalations-api';

describe('shared Supervisor queue API mode', () => {
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
      imports: [DemoSupervisorQueuePage],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        {
          provide: OPERATIONS_API,
          useFactory: () => ({
            agent: inject(AgentDisputesApi),
            supervisor: inject(SupervisorEscalationsApi),
          }),
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function render() {
    const fixture = TestBed.createComponent(DemoSupervisorQueuePage);
    fixture.detectChanges();
    return fixture;
  }

  it('loads persisted escalations and uses status, search, and opaque cursor filters', () => {
    const fixture = render();
    const initial = http.expectOne(request => request.url === '/api/v1/supervisor/escalations');
    expect(initial.request.params.get('pageSize')).toBe('20');
    expect(initial.request.params.get('status')).toBe('Open');
    initial.flush({ items: [item], pageInfo: { hasNextPage: true, nextCursor: 'opaque-cursor' } });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain(item.reference);
    expect(fixture.nativeElement.textContent).toContain(item.disputeReference);

    const root = fixture.nativeElement as HTMLElement;
    const search = root.querySelector<HTMLInputElement>('input[type="search"]')!;
    search.value = ' DSP-SUPERVISOR ';
    search.dispatchEvent(new Event('input'));
    const filtered = http.expectOne(request => request.url === '/api/v1/supervisor/escalations');
    expect(filtered.request.params.get('search')).toBe('DSP-SUPERVISOR');
    expect(filtered.request.params.get('status')).toBe('Open');
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
  });
});
