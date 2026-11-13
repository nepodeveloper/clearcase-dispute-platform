import { TestBed } from '@angular/core/testing';
import { DemoSession } from './demo-session';
import { DEMO_IDS } from './demo-data';
import { DemoWorkspace } from './demo-workspace';

describe('Isolated UI demo journeys', () => {
  let workspace: DemoWorkspace;
  let session: DemoSession;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [DemoWorkspace, DemoSession] });
    workspace = TestBed.inject(DemoWorkspace);
    session = TestBed.inject(DemoSession);
  });

  it('connects submission, escalation and supervisor review without confirming funds or exposing notes', () => {
    session.enter('customer');
    const created = workspace.submitDispute(DEMO_IDS.availableTransaction, 'Incorrect amount',
      'Please investigate this incorrect transaction amount.');
    expect(created.ok).toBe(true);
    if (!created.ok) throw new Error(created.message);
    const id = created.value.id;
    expect(workspace.customerCase(id)?.caseStatus).toBe('Submitted');

    session.enter('agent');
    const escalation = workspace.escalate(id, 'Additional investigation required',
      'Private investigation detail that must never reach the customer.');
    expect(escalation.ok).toBe(true);
    if (!escalation.ok) throw new Error(escalation.message);

    session.enter('supervisor');
    expect(workspace.acknowledge(escalation.value.id).ok).toBe(true);
    expect(workspace.resolve(escalation.value.id, 'Private supervisor resolution with next steps.').ok)
      .toBe(true);

    session.enter('customer');
    const result = workspace.customerCase(id);
    expect(result?.caseStatus).toBe('Under review');
    expect(result?.decisionStatus).toBe('Pending');
    expect(result?.financialStatus).toBe('Not initiated');
    expect(result?.timeline.at(-1)?.title).toBe('Specialist review complete');
    expect(JSON.stringify(result)).not.toContain('Private');
    expect(JSON.stringify(result)).not.toContain('customerKey');
  });

  it('prevents duplicate active disputes and validates descriptions before creating a case', () => {
    session.enter('customer');
    const before = workspace.customerCases().length;
    expect(workspace.submitDispute(DEMO_IDS.availableTransaction, 'Incorrect amount', 'short').ok)
      .toBe(false);
    const first = workspace.submitDispute(DEMO_IDS.availableTransaction, 'Incorrect amount',
      'Please investigate the amount shown on this transaction.');
    const second = workspace.submitDispute(DEMO_IDS.availableTransaction, 'Incorrect amount',
      'Please investigate the amount shown on this transaction.');
    expect(first.ok).toBe(true);
    expect(second.ok).toBe(false);
    expect(workspace.customerCases()).toHaveLength(before + 1);
  });

  it('rejects wrong-role actions, repeated escalation and out-of-order supervisor actions', () => {
    session.enter('customer');
    expect(workspace.escalate(DEMO_IDS.investigation, 'Additional investigation required',
      'This investigation requires more information.').ok).toBe(false);
    expect(workspace.acknowledge(DEMO_IDS.escalation).ok).toBe(false);
    session.enter('agent');
    expect(workspace.escalate(DEMO_IDS.timeline, 'Additional investigation required',
      'This case is already with a specialist.').ok).toBe(false);
    session.enter('supervisor');
    expect(workspace.resolve(DEMO_IDS.escalation, 'Resolution before acknowledgement.').ok).toBe(false);
    expect(workspace.acknowledge(DEMO_IDS.escalation).ok).toBe(true);
    expect(workspace.acknowledge(DEMO_IDS.escalation).ok).toBe(false);
    expect(workspace.resolve(DEMO_IDS.escalation, 'short').ok).toBe(false);
    expect(workspace.resolve(DEMO_IDS.escalation, 'Specialist review finished; investigation may continue.').ok)
      .toBe(true);
    expect(workspace.resolve(DEMO_IDS.escalation, 'Repeated specialist review resolution.').ok).toBe(false);
  });

  it('keeps other synthetic customers out of customer lists and direct links', () => {
    session.enter('customer');
    expect(workspace.customerCase(DEMO_IDS.otherCustomerCase)).toBeUndefined();
    expect(workspace.customerCases().some(item => item.id === DEMO_IDS.otherCustomerCase)).toBe(false);
    expect(workspace.transaction('unknown')).toBeUndefined();
  });

  it('starts a fresh fixture workspace rather than persisting role, notes or mutations', () => {
    session.enter('customer');
    workspace.submitDispute(DEMO_IDS.availableTransaction, 'Incorrect amount',
      'Please investigate the amount shown on this transaction.');
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [DemoWorkspace, DemoSession] });
    expect(TestBed.inject(DemoSession).role()).toBeNull();
    const fresh = TestBed.inject(DemoWorkspace);
    expect(fresh.activeDispute(DEMO_IDS.availableTransaction)).toBeUndefined();
  });
});
