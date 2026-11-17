import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AgentDisputeDetails } from './agent-disputes-api';
import { StaffCommandPanel } from './staff-command-panel';
import { suggestedStaffActions } from './agent-dispute-commands';

const id = '00000000-0000-0000-0000-000000000091';
const basic: AgentDisputeDetails = {
  id, reference: 'DSP-91', customerReference: 'CUS-91', category: 'BankCharge',
  reasonCode: 'incorrectFee', customerStatement: 'This synthetic fee needs review.',
  eligibilityOutcome: 'EligibleToSubmit', eligibilityPolicyVersion: 'test-v1',
  caseStatus: 'UnderReview', decisionStatus: 'Pending', financialStatus: 'NotInitiated',
  createdAt: '2026-11-17T08:00:00Z', version: 4,
  transaction: { description: 'Account fee', amountMinorUnits: '1000', currency: 'ZAR',
    transactionDate: '2026-11-16T08:00:00Z' },
  timeline: [], notes: [],
};

describe('Staff command UI', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [StaffCommandPanel],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  function render(value = basic) {
    const fixture = TestBed.createComponent(StaffCommandPanel);
    fixture.componentRef.setInput('dispute', value);
    fixture.detectChanges();
    return fixture;
  }

  it('requires explicit confirmation and submits the current case version', async () => {
    const fixture = render();
    let completed: string | undefined;
    fixture.componentInstance.completed.subscribe(value => { completed = value; });
    const root = fixture.nativeElement as HTMLElement;
    const approve = Array.from(root.querySelectorAll('button')).find(button =>
      button.textContent?.trim() === 'Approve dispute')!;
    approve.click();
    fixture.detectChanges();
    expect(root.textContent).toContain('Confirm: Approve dispute');
    http.expectNone(request => request.url.includes('/commands'));
    const confirm = Array.from(root.querySelectorAll('button')).find(button =>
      button.textContent?.trim() === 'Confirm Approve dispute')!;
    confirm.click();
    const req = http.expectOne(`/api/v1/agent/disputes/${id}/commands`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ command: 'Approve', expectedVersion: 4 });
    req.flush({ caseStatus: 'UnderReview', decisionStatus: 'Approved' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(completed).toBe('Approve dispute');
  });

  it('pauses during Supervisor escalation without showing command buttons', () => {
    const fixture = render({ ...basic, activeEscalation: {
      id: crypto.randomUUID(), reference: 'ESC-1', reason: 'Additional investigation required',
      status: 'Open', createdAt: basic.createdAt,
    } });
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Processing is paused');
    expect(root.textContent).not.toContain('Approve dispute');
  });

  it('recovers from conflict by requesting an authoritative refresh', async () => {
    const fixture = render();
    let refreshes = 0;
    fixture.componentInstance.refreshRequested.subscribe(() => refreshes++);
    const root = fixture.nativeElement as HTMLElement;
    Array.from(root.querySelectorAll('button')).find(x => x.textContent?.trim() === 'Reject dispute')!.click();
    fixture.detectChanges();
    Array.from(root.querySelectorAll('button')).find(x =>
      x.textContent?.trim() === 'Confirm Reject dispute')!.click();
    http.expectOne(`/api/v1/agent/disputes/${id}/commands`).flush(
      { code: 'dispute.concurrencyConflict' }, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.textContent).toContain('Refresh before attempting');
    Array.from(root.querySelectorAll('button')).find(x => x.textContent?.trim() === 'Refresh current case')!.click();
    expect(refreshes).toBe(1);
  });

  it('recommends safe sequential paths for all three categories', () => {
    expect(suggestedStaffActions({ ...basic, category: 'DebitOrder',
      caseStatus: 'ReversalConfirmed', financialStatus: 'Confirmed' }).map(x => x.command))
      .toEqual(['Close']);
    expect(suggestedStaffActions({ ...basic, category: 'BankCharge', decisionStatus: 'Approved',
      financialStatus: 'Requested' }).map(x => x.command)).toEqual(['ExecuteAdjustment']);
    expect(suggestedStaffActions({ ...basic, category: 'CardPurchase',
      cardWorkflow: { reference: 'CB-1', reasonCode: 'unrecognisedTransaction',
        status: 'MerchantAccepted', lastOperationOutcome: 'Succeeded' },
      decisionStatus: 'Approved', financialStatus: 'Requested' }).map(x => x.command))
      .toEqual(['ExecuteSettlement']);
    expect(suggestedStaffActions({ ...basic, caseStatus: 'Closed' })).toEqual([]);
  });
});
