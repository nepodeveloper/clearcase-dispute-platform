import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AgentDisputeDetails, AgentInvestigationNote } from './agent-disputes-api';
import { AgentDisputeDetailsPage } from './agent-dispute-details-page';

describe('AgentDisputeDetailsPage', () => {
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
      imports: [AgentDisputeDetailsPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id: caseId })) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function render() {
    const fixture = TestBed.createComponent(AgentDisputeDetailsPage);
    fixture.detectChanges();
    return fixture;
  }

  function respondDetails(value: AgentDisputeDetails = details) {
    http.expectOne(`/api/v1/agent/disputes/${caseId}`).flush(value);
  }

  it('loads the operational case, workflow details, timeline and private notes', () => {
    const fixture = render();
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
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('I did not authorize this synthetic collection.');
    expect(fixture.nativeElement.textContent).toContain('ZAR 100.05');
    expect(fixture.nativeElement.textContent).toContain('EftDebitOrder');
    expect(fixture.nativeElement.textContent).toContain('Customer-visible case timeline');
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('.private-notes')?.textContent)
      .toContain('Merchant verification is pending');
    expect(fixture.nativeElement.textContent).toContain('Supervisor escalation will be available in a later release');
    expect(root.querySelector('button[aria-label*="Escalate"]')).toBeNull();
  });

  it('validates notes and reuses an idempotency key for a retry', async () => {
    const fixture = render();
    respondDetails();
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const form = root.querySelector<HTMLFormElement>('form')!;
    const textarea = root.querySelector<HTMLTextAreaElement>('#investigation-note')!;

    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('15 to 2000 normalized characters');
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
    expect(root.querySelector('[data-testid="retry-note"]')).not.toBeNull();

    root.querySelector<HTMLButtonElement>('[data-testid="retry-note"]')!.click();
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
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Private investigation note saved.');
    expect(root.querySelector('.private-notes')?.textContent).toContain(note.text);
  });
});
