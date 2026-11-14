import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Dispute } from './dispute-api';
import { DisputeForm } from './dispute-form';
import { provideRouter, Router } from '@angular/router';
import { CsrfSession } from '../csrf-session';
import { sessionInterceptor } from '../auth/session-interceptor';

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
    statusDescription: 'Your dispute was received and is awaiting review.',
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
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([sessionInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    TestBed.inject(CsrfSession).update('csrf-token-from-me');
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
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

  it('submits once with CSRF and navigates to the persisted case, retaining a fallback link', () => {
    fill('unrecognisedTransaction', `  ${description}  `);
    submit();

    const request = http.expectOne('/api/v1/disputes');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('X-CSRF-TOKEN')).toBe('csrf-token-from-me');
    expect(request.request.body).toEqual({ transactionId, reasonCode: 'unrecognisedTransaction', description });
    expect((element.querySelector('[data-testid="submit-dispute"]') as HTMLButtonElement).disabled).toBe(true);
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    http.expectNone('/api/v1/disputes');
    request.flush(created, { status: 201, statusText: 'Created' });
    fixture.detectChanges();

    expect(element.querySelector('[data-testid="dispute-reference"]')?.textContent).toBe('DSP-0A1B2C3D4E5F');
    expect(TestBed.inject(Router).navigate).toHaveBeenCalledExactlyOnceWith(['/disputes', created.id]);
    expect(element.querySelector('[data-testid="track-dispute"]')?.getAttribute('href')).toBe(`/disputes/${created.id}`);
    expect(element.querySelector('form')).toBeNull();
  });

  it('also navigates to a persisted manually routed case', () => {
    fill('duplicateCharge', description);
    submit();
    http.expectOne('/api/v1/disputes').flush({ ...created, caseStatus: 'ManualReview' });
    fixture.detectChanges();

    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/disputes', created.id]);
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

  it.each([
    [400, 'Check the dispute details'],
    [401, 'Sign in again'],
    [403, 'You do not have permission'],
  ])('explains HTTP %s without navigating to a case', (status, message) => {
    fill('duplicateCharge', description);
    submit();
    http.expectOne('/api/v1/disputes').flush({}, { status, statusText: 'Rejected' });
    fixture.detectChanges();

    expect(element.querySelector('[role="alert"]')?.textContent).toContain(message);
    if (status === 401) {
      expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/' } });
    } else {
      expect(TestBed.inject(Router).navigate).not.toHaveBeenCalled();
    }
  });

  it.each([400, 422])('associates HTTP %s server field errors with the form', (status) => {
    fill('duplicateCharge', description);
    submit();
    http.expectOne('/api/v1/disputes').flush({
      errors: [{ pointer: '/reasonCode', code: 'reasonCode.invalid', message: 'Select a reason that applies to this transaction.' }],
    }, { status, statusText: 'Invalid' });
    fixture.detectChanges();

    expect(element.querySelector('#dispute-reason-error')?.textContent).toContain('Select a reason that applies');
    expect(element.querySelector('#dispute-reason')?.getAttribute('aria-invalid')).toBe('true');
    fill('unrecognisedTransaction', description);
    submit();
    http.expectOne('/api/v1/disputes').flush(created);
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
