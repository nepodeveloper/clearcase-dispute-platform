import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Dispute } from './dispute-api';
import { DisputeForm } from './dispute-form';

describe('DisputeForm', () => {
  const transactionId = '00000000-0000-0000-0000-000000000001';
  const description = 'I did not make this purchase at the store.';
  const created: Dispute = {
    id: 'a5b4f0c2-9a1d-4c77-8d43-3f1e8f0c2b11',
    reference: 'DSP-0A1B2C3D4E5F',
    transactionId,
    category: 'CardPurchase',
    reasonCode: 'unrecognisedTransaction',
    description,
    caseStatus: 'Submitted',
    decisionStatus: 'Pending',
    financialStatus: 'NotInitiated',
    createdAt: '2026-11-12T10:00:00Z',
    timeline: [{
      id: 'f7d8c9b0-1111-4222-8333-444455556666',
      sequenceNumber: 1,
      eventType: 'DisputeSubmitted',
      caseStatus: 'Submitted',
      description: 'Your dispute was received and is awaiting review.',
      occurredAt: '2026-11-12T10:00:00Z',
    }],
  };
  let http: HttpTestingController;
  let fixture: ComponentFixture<DisputeForm>;
  let element: HTMLElement;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(DisputeForm);
    fixture.componentRef.setInput('transactionId', transactionId);
    fixture.componentRef.setInput('reasons', [
      { code: 'unrecognisedTransaction', label: 'I do not recognise this transaction' },
      { code: 'duplicateCharge', label: 'I was charged more than once' },
    ]);
    fixture.detectChanges();
    element = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => http.verify());

  it('lists the eligible reasons', () => {
    const options = Array.from(element.querySelectorAll('option')).map((option) => option.textContent?.trim());

    expect(options).toEqual([
      'Select a reason',
      'I do not recognise this transaction',
      'I was charged more than once',
    ]);
  });

  it('does not submit until a reason and sufficient description are provided', () => {
    fill('', 'too short');
    submit();

    http.expectNone('/api/v1/disputes');
    expect(element.textContent).toContain('Select a reason.');
    expect(element.textContent).toContain('at least 15 characters');
    expect(element.querySelector('#dispute-reason')?.getAttribute('aria-invalid')).toBe('true');
  });

  it('submits the dispute and shows the case reference and initial status', () => {
    fill('unrecognisedTransaction', `  ${description}  `);
    submit();

    const request = http.expectOne('/api/v1/disputes');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ transactionId, reasonCode: 'unrecognisedTransaction', description });
    expect((element.querySelector('[data-testid="submit-dispute"]') as HTMLButtonElement).disabled).toBe(true);
    request.flush(created, { status: 201, statusText: 'Created' });
    fixture.detectChanges();

    expect(element.querySelector('[data-testid="dispute-reference"]')?.textContent).toBe('DSP-0A1B2C3D4E5F');
    expect(element.textContent).toContain('Submitted - awaiting review');
    expect(element.textContent).toContain('Your dispute was received and is awaiting review.');
    expect(element.querySelector('form')).toBeNull();
  });

  it('shows the specialist-review status for a manually routed case', () => {
    fill('duplicateCharge', description);
    submit();
    http.expectOne('/api/v1/disputes').flush({ ...created, caseStatus: 'ManualReview' });
    fixture.detectChanges();

    expect(element.textContent).toContain('Referred for specialist review');
  });

  it('explains an existing open dispute and keeps the form available', () => {
    fill('duplicateCharge', description);
    submit();
    http.expectOne('/api/v1/disputes').flush(
      { code: 'dispute.activeCaseExists' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(element.querySelector('[data-testid="dispute-error"]')?.textContent)
      .toContain('already has an open dispute');
    expect((element.querySelector('[data-testid="submit-dispute"]') as HTMLButtonElement).disabled).toBe(false);
  });

  it('refreshes processing and timeline without implying approved funds are confirmed', () => {
    fill('duplicateCharge', description);
    submit();
    http.expectOne('/api/v1/disputes').flush(created);
    fixture.detectChanges();

    (element.querySelector('[data-testid="refresh-dispute"]') as HTMLButtonElement).click();
    const request = http.expectOne(`/api/v1/disputes/${created.id}`);
    expect(request.request.method).toBe('GET');
    request.flush({
      ...created, category: 'DebitOrder', caseStatus: 'ReversalRequested',
      decisionStatus: 'Approved', financialStatus: 'OutcomeUnknown',
      timeline: [...created.timeline, {
        ...created.timeline[0], id: 'another-event', sequenceNumber: 2,
        caseStatus: 'ReversalRequested', description: 'The simulated outcome is unknown.',
      }],
    });
    fixture.detectChanges();

    expect(element.textContent).toContain('Reversal requested');
    expect(element.textContent).toContain('Decision: Approved');
    expect(element.textContent).toContain('Outcome unknown - not confirmed');
    expect(element.querySelectorAll('ol li')).toHaveLength(2);
    http.expectNone((req) => req.url.includes('simulations'));
  });

  it('keeps the case visible and allows retry when status refresh fails', () => {
    fill('duplicateCharge', description);
    submit();
    http.expectOne('/api/v1/disputes').flush(created);
    fixture.detectChanges();
    (element.querySelector('[data-testid="refresh-dispute"]') as HTMLButtonElement).click();
    http.expectOne(`/api/v1/disputes/${created.id}`).flush({}, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();

    expect(element.textContent).toContain(created.reference);
    expect(element.querySelector('[role="alert"]')?.textContent).toContain('Unable to refresh');
    expect((element.querySelector('[data-testid="refresh-dispute"]') as HTMLButtonElement).disabled).toBe(false);
  });

  it('shows chargeback acceptance separately from an explicit simulated settlement result', () => {
    fill('duplicateCharge', description);
    submit();
    http.expectOne('/api/v1/disputes').flush(created);
    fixture.detectChanges();
    const accepted = {
      ...created, caseStatus: 'UnderReview', decisionStatus: 'Approved',
      chargeback: {
        reference: 'MOCK-CB-test', reasonCode: 'duplicateCharge',
        status: 'MerchantAccepted', lastOperationOutcome: 'Succeeded',
      },
    };
    (element.querySelector('[data-testid="refresh-dispute"]') as HTMLButtonElement).click();
    http.expectOne(`/api/v1/disputes/${created.id}`).flush(accepted);
    fixture.detectChanges();

    expect(element.textContent).toContain('MOCK-CB-test');
    expect(element.textContent).toContain('Merchant accepted');
    expect(element.textContent).toContain('Decision: Approved');
    expect(element.textContent).toContain('Financial status: Not initiated');
    expect(element.textContent).not.toContain('settlement confirmed');

    (element.querySelector('[data-testid="refresh-dispute"]') as HTMLButtonElement).click();
    http.expectOne(`/api/v1/disputes/${created.id}`).flush({ ...accepted, financialStatus: 'Confirmed' });
    fixture.detectChanges();

    expect(element.textContent).toContain('Simulated settlement confirmed - no real money moved');
    expect(element.textContent).not.toContain('reversal confirmed');
    http.expectNone((request) => request.url.includes('simulations'));
  });

  it('shows the server explanation for an ineligible submission', () => {
    fill('duplicateCharge', description);
    submit();
    http.expectOne('/api/v1/disputes').flush(
      { detail: 'This card transaction is still a pending authorization.' },
      { status: 422, statusText: 'Unprocessable Entity' });
    fixture.detectChanges();

    expect(element.querySelector('[data-testid="dispute-error"]')?.textContent)
      .toContain('still a pending authorization');
  });

  function fill(reasonCode: string, text: string): void {
    const select = element.querySelector('#dispute-reason') as HTMLSelectElement;
    select.value = reasonCode;
    select.dispatchEvent(new Event('change'));
    const textarea = element.querySelector('#dispute-description') as HTMLTextAreaElement;
    textarea.value = text;
    textarea.dispatchEvent(new Event('input'));
  }

  function submit(): void {
    (element.querySelector('[data-testid="submit-dispute"]') as HTMLButtonElement).click();
    fixture.detectChanges();
  }
});
