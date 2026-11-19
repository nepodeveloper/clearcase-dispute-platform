import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AgentDisputeDetails, AgentEscalationCreation, AgentInvestigationNote } from './agent-disputes-api';
import { EscalationForm } from './escalation-form';
import { InvestigationPage } from './investigation-page';

describe('Agent investigation', () => {
  const caseId = '00000000-0000-0000-0000-000000000001';
  const details: AgentDisputeDetails = {
    id: caseId,
    reference: 'DSP-AGENT000001',
    customerReference: 'CUS-7B2A31D940E2',
    category: 'DebitOrder',
    reasonCode: 'unauthorisedDebitOrder',
    customerStatement: 'I did not authorize this synthetic collection.',
    eligibilityOutcome: 'RequiresManualReview',
    eligibilityPolicyVersion: 'policy-v1',
    caseStatus: 'ManualReview',
    decisionStatus: 'Pending',
    financialStatus: 'NotInitiated',
    createdAt: '2026-11-12T10:00:00Z',
    version: 1,
    transaction: {
      description: 'Synthetic debit order',
      amountMinorUnits: '10005',
      currency: 'ZAR',
      transactionDate: '2026-11-11T09:00:00Z',
      mandateType: 'EftDebitOrder',
      collectionDate: '2026-11-11',
      mandateAuthorised: false,
      mandateMatchesCollection: false,
    },
    availableActions: [{
      command: 'StartReview', label: 'Start review', description: 'Begin investigating this debit order.',
      movesMoney: false, terminal: false,
    }],
    timeline: [{
      id: '00000000-0000-0000-0000-000000000002',
      sequenceNumber: 1,
      eventType: 'DisputeSubmitted',
      caseStatus: 'ManualReview',
      description: 'Your dispute was received and will be reviewed.',
      occurredAt: '2026-11-12T10:00:00Z',
    }],
    notes: [],
  };
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [InvestigationPage, EscalationForm],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function renderDetails() {
    const fixture = TestBed.createComponent(InvestigationPage);
    fixture.componentRef.setInput('id', caseId);
    fixture.detectChanges();
    return fixture;
  }

  function respondDetails(value: AgentDisputeDetails = details) {
    http.expectOne(`/api/v1/agent/disputes/${caseId}`).flush(value);
  }

  it('loads persisted workflow details while keeping internal notes out of the public timeline', async () => {
    const fixture = renderDetails();
    respondDetails({
      ...details,
      notes: [{
        id: '00000000-0000-0000-0000-000000000003',
        disputeId: caseId,
        employeeId: '33333333-3333-3333-3333-333333333333',
        text: 'Merchant verification is pending before decision.',
        createdAt: '2026-11-12T11:00:00Z',
        correlationId: 'note-correlation',
      }],
    });
    await fixture.whenStable();
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('I did not authorize this synthetic collection.');
    expect(root.textContent).toContain('R 100.05');
    expect(root.textContent).toContain('EftDebitOrder');
    expect(root.querySelector('.private-notes')?.textContent).toContain('Merchant verification is pending');
    expect(root.querySelector('[aria-label="Customer-visible case timeline"]')?.textContent)
      .not.toContain('Merchant verification is pending');
    expect(root.textContent).toContain('Escalate case');
    expect(Array.from(root.querySelectorAll('button')).some(b => b.textContent?.trim() === 'Start review')).toBe(true);
    expect(root.querySelector('#escalation-note')).toBeNull();
  });

  it('reuses an idempotency key when retrying the same private note', async () => {
    const fixture = renderDetails();
    respondDetails();
    await fixture.whenStable();
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const form = root.querySelector<HTMLFormElement>('.private-notes form')!;
    const textarea = root.querySelector<HTMLTextAreaElement>('#agent-note')!;

    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('15 and 2000 normalized characters');
    http.expectNone(request => request.url.includes('/notes'));

    textarea.value = '  Please verify the merchant authorization record.  ';
    textarea.dispatchEvent(new Event('input'));
    form.dispatchEvent(new Event('submit'));
    const first = http.expectOne(`/api/v1/agent/disputes/${caseId}/notes`);
    const key = first.request.headers.get('Idempotency-Key');
    expect(first.request.body.text).toBe('Please verify the merchant authorization record.');
    first.flush({}, { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.querySelector('.cc-form-error')).not.toBeNull();

    form.dispatchEvent(new Event('submit'));
    const retry = http.expectOne(`/api/v1/agent/disputes/${caseId}/notes`);
    expect(retry.request.headers.get('Idempotency-Key')).toBe(key);
    const note: AgentInvestigationNote = {
      id: '00000000-0000-0000-0000-000000000003',
      disputeId: caseId,
      employeeId: '33333333-3333-3333-3333-333333333333',
      text: 'Please verify the merchant authorization record.',
      createdAt: '2026-11-12T11:00:00Z',
      correlationId: 'note-correlation',
    };
    retry.flush(note);
    await fixture.whenStable();
    http.expectOne(`/api/v1/agent/disputes/${caseId}`).flush({ ...details, notes: [note] });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.textContent).toContain('Internal investigation note saved.');
    expect(root.querySelector('.private-notes')?.textContent).toContain(note.text);
  });

  it('submits escalations with an idempotency key and preserves the exact request on retry', async () => {
    const fixture = TestBed.createComponent(EscalationForm);
    fixture.componentRef.setInput('caseId', caseId);
    fixture.componentRef.setInput('reference', details.reference);
    fixture.detectChanges();
    let completed: AgentEscalationCreation | null = null;
    fixture.componentInstance.completed.subscribe(result => { completed = result; });
    const root = fixture.nativeElement as HTMLElement;
    const textarea = root.querySelector<HTMLTextAreaElement>('#escalation-note')!;
    const form = root.querySelector<HTMLFormElement>('form')!;

    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    expect(root.textContent).toContain('15 and 2000 normalized characters');
    http.expectNone(request => request.url.includes('/escalations'));

    textarea.value = '  Please complete an independent authorization review.  ';
    textarea.dispatchEvent(new Event('input'));
    form.dispatchEvent(new Event('submit'));
    const first = http.expectOne(`/api/v1/agent/disputes/${caseId}/escalations`);
    const key = first.request.headers.get('Idempotency-Key');
    expect(first.request.body).toEqual({
      reason: 'Additional investigation required',
      note: 'Please complete an independent authorization review.',
    });
    first.flush({}, { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();
    fixture.detectChanges();

    form.dispatchEvent(new Event('submit'));
    const retry = http.expectOne(`/api/v1/agent/disputes/${caseId}/escalations`);
    expect(retry.request.headers.get('Idempotency-Key')).toBe(key);
    const escalation: AgentEscalationCreation = {
      id: '00000000-0000-0000-0000-000000000004',
      reference: 'ESC-00000000000000000000000000000004',
      disputeId: caseId,
      reason: 'Additional investigation required',
      status: 'Open',
      createdAt: '2026-11-12T11:00:00Z',
    };
    retry.flush(escalation);
    await fixture.whenStable();
    expect(completed).toEqual(escalation);
  });
});
