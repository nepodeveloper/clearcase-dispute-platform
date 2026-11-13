import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { DEMO_IDS } from './demo-data';
import { demoRoutes } from './demo.routes';
import { DemoSession } from './demo-session';
import { DemoWorkspace } from './demo-workspace';

describe('UI demo screen journeys', () => {
  let session: DemoSession;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter(demoRoutes, withComponentInputBinding()), DemoSession, DemoWorkspace],
    });
    session = TestBed.inject(DemoSession);
    router = TestBed.inject(Router);
  });

  function element<T extends Element>(harness: RouterTestingHarness, selector: string): T {
    const result = harness.routeNativeElement?.querySelector<T>(selector);
    if (result === undefined || result === null) throw new Error(`Missing element: ${selector}`);
    return result;
  }

  function fill(harness: RouterTestingHarness, selector: string, value: string): void {
    const field = element<HTMLInputElement | HTMLTextAreaElement>(harness, selector);
    field.value = value;
    field.dispatchEvent(new Event('input'));
    harness.detectChanges();
  }

  async function submit(harness: RouterTestingHarness): Promise<void> {
    element<HTMLFormElement>(harness, 'form').dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await harness.fixture.whenStable();
    harness.detectChanges();
  }

  it('requires a demo role, rejects the wrong journey and renders an unknown route safely', async () => {
    const harness = await RouterTestingHarness.create('/agent/disputes');
    expect(router.url).toBe('/login');
    expect(element<HTMLInputElement>(harness, '#login-password').readOnly).toBe(true);
    await submit(harness);
    expect(router.url).toBe('/');
    await harness.navigateByUrl('/agent/escalations');
    expect(router.url).toBe('/unavailable');
    expect(harness.routeNativeElement?.textContent).toContain('Choose the matching demo role');
    await harness.navigateByUrl('/missing-screen');
    expect(harness.routeNativeElement?.textContent).toContain('Page not found');
  });

  it('filters and paginates transactions, announces no matches, and resets to page one', async () => {
    session.enter('customer');
    const harness = await RouterTestingHarness.create('/');
    expect(harness.routeNativeElement?.querySelectorAll('tbody tr')).toHaveLength(6);
    element<HTMLButtonElement>(harness, '[aria-label="Next page"]').click();
    harness.detectChanges();
    expect(element<HTMLElement>(harness, '.panel-footer').textContent).toContain('7–12 of 24');
    expect(document.activeElement?.id).toBe('transactions-heading');
    fill(harness, 'input[type="search"]', 'R 750.00');
    expect(harness.routeNativeElement?.querySelectorAll('tbody tr')).toHaveLength(1);
    expect(harness.routeNativeElement?.textContent).toContain('Woolworths');
    expect(element<HTMLElement>(harness, '.panel-footer').textContent).toContain('1–1 of 1');
    fill(harness, 'input[type="search"]', 'no such merchant');
    expect(element<HTMLElement>(harness, '[role="status"]').textContent).toContain('No transactions found');
  });

  it('validates and submits a dispute, updates its dashboard, and handles reused detail routes', async () => {
    session.enter('customer');
    const harness = await RouterTestingHarness.create(`/transactions/${DEMO_IDS.availableTransaction}`);
    await submit(harness);
    expect(element<HTMLElement>(harness, '[role="alert"]').textContent).toContain('between 15 and 2000');
    fill(harness, '#dispute-description', 'Please investigate this incorrect amount for the demo.');
    await submit(harness);
    expect(router.url).toMatch(/^\/disputes\//);
    expect(harness.routeNativeElement?.textContent).toContain('Dispute submitted');
    expect(harness.routeNativeElement?.textContent).toContain('Not initiated');
    const reference = TestBed.inject(DemoWorkspace).activeDispute(DEMO_IDS.availableTransaction)?.reference;
    await harness.navigateByUrl('/disputes');
    expect(harness.routeNativeElement?.textContent).toContain(reference);
    await harness.navigateByUrl(`/transactions/${DEMO_IDS.availableTransaction}`);
    expect(harness.routeNativeElement?.textContent).toContain('A dispute is already in progress');
    await harness.navigateByUrl(`/disputes/${DEMO_IDS.timeline}`);
    expect(harness.routeNativeElement?.textContent).toContain('CC-2026-01457');
    await harness.navigateByUrl(`/disputes/${DEMO_IDS.otherCustomerCase}`);
    expect(harness.routeNativeElement?.textContent).toContain('Dispute not found');
    expect(harness.routeNativeElement?.textContent).not.toContain('CC-2026-01457');
  });

  it('connects escalation and supervisor forms to a safe customer timeline update', async () => {
    session.enter('agent');
    const harness = await RouterTestingHarness.create(`/agent/disputes/${DEMO_IDS.investigation}/escalate`);
    await submit(harness);
    expect(element<HTMLElement>(harness, '[role="alert"]').textContent).toContain('between 15 and 2000');
    fill(harness, '#escalation-note', 'Private operational finding, never for the customer.');
    await submit(harness);
    expect(harness.routeNativeElement?.textContent).toContain('created');
    element<HTMLButtonElement>(harness, '.cc-button--primary').click();
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(router.url).toMatch(/^\/agent\/escalations\//);
    expect(harness.routeNativeElement?.textContent).toContain('Private operational finding');
    expect(element<HTMLButtonElement>(harness, 'button[type="submit"]').disabled).toBe(true);
    element<HTMLButtonElement>(harness, 'button.cc-button--secondary').click();
    harness.detectChanges();
    await submit(harness);
    expect(element<HTMLElement>(harness, '[role="alert"]').textContent).toContain('between 15 and 2000');
    fill(harness, '#resolution-note', 'Private supervisor findings with recommended next steps.');
    await submit(harness);
    expect(harness.routeNativeElement?.textContent).toContain('Demo escalation resolved');
    expect(harness.routeNativeElement?.querySelector('button[type="submit"]')).toBeNull();
    session.enter('customer');
    await harness.navigateByUrl(`/disputes/${DEMO_IDS.investigation}`);
    const text = harness.routeNativeElement?.textContent;
    expect(text).toContain('Specialist review complete');
    expect(text).toContain('Under review');
    expect(text).toContain('Not initiated');
    expect(text).not.toContain('Private');
  });

  it('does not carry an internal draft or error to another escalation', async () => {
    session.enter('supervisor');
    const harness = await RouterTestingHarness.create(`/agent/escalations/${DEMO_IDS.escalation}`);
    fill(harness, '#resolution-note', 'Draft for the first escalation only.');
    const other = TestBed.inject(DemoWorkspace).escalations()[1];
    await harness.navigateByUrl(`/agent/escalations/${other.id}`);
    expect(element<HTMLTextAreaElement>(harness, '#resolution-note').value).toBe('');
  });
});
