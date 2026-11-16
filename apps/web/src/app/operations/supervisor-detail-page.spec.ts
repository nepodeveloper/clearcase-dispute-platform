import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SupervisorDetailPage } from './supervisor-detail-page';
import { SupervisorEscalation } from './supervisor-escalations-api';

describe('Supervisor escalation review', () => {
  const escalationId = '00000000-0000-0000-0000-000000000010';
  const details: SupervisorEscalation = {
    id: escalationId,
    reference: 'ESC-00000000000000000000000000000010',
    disputeId: '00000000-0000-0000-0000-000000000011',
    disputeReference: 'DSP-SUPERVISOR-CASE',
    customerReference: 'CUS-7B2A31D940E2',
    category: 'CardPurchase',
    caseStatus: 'ManualReview',
    decisionStatus: 'Pending',
    financialStatus: 'NotInitiated',
    reason: 'Policy exception review',
    note: 'Initial checks require an independent review.',
    status: 'Open',
    createdByEmployeeId: '33333333-3333-3333-3333-333333333333',
    createdAt: '2026-11-12T10:00:00Z',
    history: [{
      id: '00000000-0000-0000-0000-000000000012',
      action: 'Created',
      employeeId: '33333333-3333-3333-3333-333333333333',
      privateNote: 'Initial checks require an independent review.',
      occurredAt: '2026-11-12T10:00:00Z',
      correlationId: 'create-correlation',
    }],
  };
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [SupervisorDetailPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('requires acknowledgement before resolution and leaves decision and financial statuses unchanged', async () => {
    const fixture = TestBed.createComponent(SupervisorDetailPage);
    fixture.componentRef.setInput('id', escalationId);
    fixture.detectChanges();
    http.expectOne(`/api/v1/supervisor/escalations/${escalationId}`).flush(details);
    await fixture.whenStable();
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Initial checks require an independent review.');
    expect(root.textContent).toContain('NotInitiated');
    const acknowledgeButton = [...root.querySelectorAll<HTMLButtonElement>('button')]
      .find(button => button.textContent?.includes('Acknowledge escalation'))!;
    acknowledgeButton.click();
    const acknowledgement = http.expectOne(
      `/api/v1/supervisor/escalations/${escalationId}/acknowledgements`,
    );
    expect(acknowledgement.request.headers.has('Idempotency-Key')).toBe(true);
    const acknowledged: SupervisorEscalation = {
      ...details,
      status: 'Acknowledged',
      history: [...details.history, {
        id: '00000000-0000-0000-0000-000000000013',
        action: 'Acknowledged',
        employeeId: '33333333-3333-3333-3333-333333333334',
        occurredAt: '2026-11-12T11:00:00Z',
        correlationId: 'ack-correlation',
      }],
    };
    acknowledgement.flush(acknowledged);
    await fixture.whenStable();
    http.expectOne(`/api/v1/supervisor/escalations/${escalationId}`).flush(acknowledged);
    await fixture.whenStable();
    fixture.detectChanges();

    const note = root.querySelector<HTMLTextAreaElement>('#resolution-note')!;
    note.value = 'Independent checks are complete; investigation may continue.';
    note.dispatchEvent(new Event('input'));
    const form = note.closest('form')!;
    form.dispatchEvent(new Event('submit'));
    const resolution = http.expectOne(
      `/api/v1/supervisor/escalations/${escalationId}/resolutions`,
    );
    expect(resolution.request.body.note).toBe(note.value);
    expect(resolution.request.headers.has('Idempotency-Key')).toBe(true);
    const resolved: SupervisorEscalation = {
      ...acknowledged,
      status: 'Resolved',
      caseStatus: 'UnderReview',
      history: [...acknowledged.history, {
        id: '00000000-0000-0000-0000-000000000014',
        action: 'Resolved',
        employeeId: '33333333-3333-3333-3333-333333333334',
        privateNote: note.value,
        occurredAt: '2026-11-12T12:00:00Z',
        correlationId: 'resolve-correlation',
      }],
    };
    resolution.flush(resolved);
    await fixture.whenStable();
    http.expectOne(`/api/v1/supervisor/escalations/${escalationId}`).flush(resolved);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.textContent).toContain('The linked dispute remains under review');
    expect(root.textContent).toContain('no funds are confirmed');
    expect(root.textContent).toContain('Independent checks are complete; investigation may continue.');
    expect(root.textContent).toContain('Pending');
    expect(root.textContent).toContain('NotInitiated');
  });
});
