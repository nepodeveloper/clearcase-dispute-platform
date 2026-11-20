import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { installDialogPolyfill } from '../testing/dialog-polyfill';
import { Dispute } from './dispute-api';
import { DisputeWithdrawal } from './dispute-withdrawal';

describe('DisputeWithdrawal', () => {
  const dispute = {
    id: 'aaaaaaaa-1111-4222-8333-444455556666', reference: 'DSP-WITHDRAW', version: 3, canWithdraw: true,
    caseStatus: 'Submitted', decisionStatus: 'Pending', financialStatus: 'NotInitiated',
  } as unknown as Dispute;
  const url = `/api/v1/disputes/${dispute.id}/withdrawals`;
  let http: HttpTestingController;

  beforeAll(() => installDialogPolyfill());

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(value: Dispute = dispute) {
    const fixture = TestBed.createComponent(DisputeWithdrawal);
    fixture.componentRef.setInput('dispute', value);
    const withdrawn: Dispute[] = [];
    let refreshes = 0;
    fixture.componentInstance.withdrawn.subscribe(result => withdrawn.push(result));
    fixture.componentInstance.refreshRequested.subscribe(() => refreshes++);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    return { fixture, root, withdrawn, refreshes: () => refreshes,
      dialog: root.querySelector<HTMLDialogElement>('dialog')!,
      async open() {
        const button = root.querySelector<HTMLButtonElement>('[data-testid="withdraw-dispute"]')!;
        button.focus();
        button.click();
        await fixture.whenStable();
        return button;
      },
      form: () => root.querySelector<HTMLFormElement>('dialog form')!,
      async submit(reason?: string) {
        if (reason !== undefined) {
          const textarea = root.querySelector<HTMLTextAreaElement>('#withdraw-reason')!;
          textarea.value = reason;
          textarea.dispatchEvent(new Event('input'));
        }
        root.querySelector<HTMLFormElement>('dialog form')!.dispatchEvent(new Event('submit', { cancelable: true }));
        await fixture.whenStable();
      } };
  }

  it('offers withdrawal only when the server says it is allowed', async () => {
    const hidden = await render({ ...dispute, canWithdraw: false });
    expect(hidden.root.querySelector('[data-testid="withdraw-dispute"]')).toBeNull();
    const missing = await render({ ...dispute, canWithdraw: undefined });
    expect(missing.root.querySelector('[data-testid="withdraw-dispute"]')).toBeNull();
  });

  it('confirms in a modal dialog, sends the version and reason, and reports success', async () => {
    const view = await render();
    await view.open();
    expect(view.dialog.hasAttribute('open')).toBe(true);
    expect(view.dialog.getAttribute('aria-labelledby')).toBe('withdraw-heading');
    expect(view.dialog.textContent).toContain('no payment adjustment will be made');

    const submit = view.submit('Bought it twice by mistake  ');
    const request = http.expectOne(url);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ expectedVersion: 3, reason: 'Bought it twice by mistake' });
    const closed = { ...dispute, caseStatus: 'Closed', canWithdraw: false, version: 4 } as unknown as Dispute;
    request.flush(closed);
    await submit;
    view.fixture.detectChanges();

    expect(view.withdrawn).toEqual([closed]);
    expect(view.dialog.hasAttribute('open')).toBe(false);
    const outcome = view.root.querySelector('[data-testid="withdraw-outcome"]')!;
    expect(outcome.getAttribute('role')).toBe('status');
    expect(outcome.textContent).toContain('You withdrew this dispute. No payment adjustment was made.');
  });

  it('omits an empty reason from the request', async () => {
    const view = await render();
    await view.open();
    const submit = view.submit('   ');
    const request = http.expectOne(url);
    expect(request.request.body).toEqual({ expectedVersion: 3 });
    request.flush({ ...dispute, canWithdraw: false });
    await submit;
  });

  it('returns focus to the opening button when the dialog is cancelled', async () => {
    const view = await render();
    const trigger = await view.open();
    view.root.querySelector<HTMLButtonElement>('button[aria-label="Cancel withdrawal"]')!.click();
    await view.fixture.whenStable();
    expect(view.dialog.hasAttribute('open')).toBe(false);
    expect(document.activeElement).toBe(trigger);
    expect(view.withdrawn).toHaveLength(0);
  });

  it('traps Tab inside the open dialog', async () => {
    const view = await render();
    await view.open();
    const controls = view.dialog.querySelectorAll<HTMLElement>('button:not(:disabled), textarea:not(:disabled)');
    const last = controls[controls.length - 1];
    const first = controls[0];
    last.focus();
    last.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(first);
    first.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(last);
  });

  for (const [code, expected] of [
    ['dispute.withdrawalNotAllowed', 'can no longer be withdrawn'],
    ['dispute.concurrencyConflict', 'changed while you were viewing it'],
  ] as const) {
    it(`explains ${code}, closes the dialog and asks the page to refresh`, async () => {
      const view = await render();
      await view.open();
      const submit = view.submit();
      http.expectOne(url).flush({ code }, { status: 409, statusText: 'Conflict' });
      await submit;
      view.fixture.detectChanges();
      expect(view.refreshes()).toBe(1);
      expect(view.withdrawn).toHaveLength(0);
      expect(view.dialog.hasAttribute('open')).toBe(false);
      const outcome = view.root.querySelector('[data-testid="withdraw-outcome"]')!;
      expect(outcome.getAttribute('role')).toBe('alert');
      expect(outcome.textContent).toContain(expected);
    });
  }

  it('keeps the dialog open on a transient failure and succeeds on retry', async () => {
    const view = await render();
    await view.open();
    const first = view.submit();
    http.expectOne(url).flush({}, { status: 503, statusText: 'Unavailable' });
    await first;
    view.fixture.detectChanges();
    expect(view.dialog.hasAttribute('open')).toBe(true);
    expect(view.dialog.querySelector('[role="alert"]')?.textContent).toContain('Nothing was changed');

    const retry = view.submit();
    http.expectOne(url).flush({ ...dispute, canWithdraw: false });
    await retry;
    expect(view.withdrawn).toHaveLength(1);
  });
});
