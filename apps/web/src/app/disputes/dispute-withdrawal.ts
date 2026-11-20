import { HttpErrorResponse } from '@angular/common/http';
import { afterNextRender, Component, ElementRef, inject, Injector, input, output, signal, viewChild } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { Dispute, DisputeApi, problemCode } from './dispute-api';

export const withdrawalReasonMaxLength = 500;

type Outcome = { kind: 'success' | 'error'; text: string };

/** Withdraw action and confirmation dialog. Whether a withdrawal is allowed is decided by the server (canWithdraw). */
@Component({
  selector: 'cc-dispute-withdrawal',
  imports: [ReactiveFormsModule],
  template: `
    @if (dispute().canWithdraw) {
      <div class="withdraw-action">
        <button class="cc-button cc-button--secondary" type="button" data-testid="withdraw-dispute"
          (click)="openDialog()">Withdraw dispute</button>
        <span class="muted">Changed your mind? Withdrawing closes this dispute and makes no payment adjustment.</span>
      </div>
    }
    @if (outcome(); as result) {
      @if (result.kind === 'success') {
        <p class="notice" role="status" data-testid="withdraw-outcome">{{ result.text }}</p>
      } @else {
        <p class="cc-form-error" role="alert" data-testid="withdraw-outcome">{{ result.text }}</p>
      }
    }
    <dialog #withdrawDialog class="cc-dialog" aria-labelledby="withdraw-heading"
      (keydown)="dialogKeydown($event)" (close)="dialogClosed()">
      @if (dialogOpen()) {
        <div class="modal-head">
          <h2 id="withdraw-heading">Withdraw this dispute?</h2>
          <button class="icon-button" type="button" aria-label="Cancel withdrawal" (click)="closeDialog()">&times;</button>
        </div>
        <p>You are about to withdraw dispute <strong>{{ dispute().reference }}</strong>. The case will be closed and
          <strong>no payment adjustment will be made</strong>. You can open a new dispute for this transaction afterwards.</p>
        <form class="form-stack" (submit)="submit($event)" novalidate>
          <div class="cc-field"><label for="withdraw-reason">Reason (optional)</label>
            <textarea id="withdraw-reason" class="cc-control" rows="3" [formControl]="reason" autofocus
              [attr.maxlength]="maxLength" aria-describedby="withdraw-reason-hint withdraw-error"
              [attr.aria-invalid]="error() !== ''"></textarea>
            <span class="field-hint" id="withdraw-reason-hint">Up to {{ maxLength }} characters. Do not include card or account numbers.</span>
          </div>
          <div id="withdraw-error">@if (error()) { <p class="cc-form-error" role="alert">{{ error() }}</p> }</div>
          <div class="button-row align-right">
            <button class="cc-button cc-button--secondary" type="button" [disabled]="pending()" (click)="closeDialog()">Keep dispute</button>
            <button class="cc-button cc-button--danger" type="submit" [disabled]="pending()">
              {{ pending() ? 'Withdrawing...' : 'Withdraw dispute' }}</button>
          </div>
        </form>
      }
    </dialog>`,
})
export class DisputeWithdrawal {
  readonly dispute = input.required<Dispute>();
  readonly withdrawn = output<Dispute>();
  readonly refreshRequested = output<void>();
  private readonly api = inject(DisputeApi);
  private readonly injector = inject(Injector);
  private readonly dialog = viewChild<ElementRef<HTMLDialogElement>>('withdrawDialog');
  private opener: HTMLElement | null = null;
  protected readonly maxLength = withdrawalReasonMaxLength;
  protected readonly dialogOpen = signal(false);
  protected readonly pending = signal(false);
  protected readonly error = signal('');
  protected readonly outcome = signal<Outcome | null>(null);
  protected readonly reason = new FormControl('', { nonNullable: true, validators: Validators.maxLength(withdrawalReasonMaxLength) });

  protected openDialog(): void {
    this.opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    this.reason.reset();
    this.error.set('');
    this.outcome.set(null);
    this.dialogOpen.set(true);
    afterNextRender(() => this.dialog()?.nativeElement.showModal(), { injector: this.injector });
  }

  protected closeDialog(): void {
    if (!this.pending()) this.dialog()?.nativeElement.close();
  }

  protected dialogKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape' && this.pending()) { event.preventDefault(); return; }
    if (event.key !== 'Tab') return;
    const controls = this.dialog()?.nativeElement.querySelectorAll<HTMLElement>(
      'button:not(:disabled), select:not(:disabled), textarea:not(:disabled)');
    if (controls === undefined || controls.length === 0) return;
    const first = controls[0];
    const last = controls[controls.length - 1];
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
  }

  protected dialogClosed(): void {
    this.dialogOpen.set(false);
    const opener = this.opener;
    // The Withdraw button is removed once the dispute is closed, so fall back to the page heading.
    afterNextRender(() => {
      if (opener?.isConnected === true) opener.focus();
      else document.querySelector<HTMLElement>('main h1')?.focus();
    }, { injector: this.injector });
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.pending()) return;
    const reason = this.reason.value.trim();
    if (reason.length > withdrawalReasonMaxLength) {
      this.error.set(`Keep the reason to ${withdrawalReasonMaxLength} characters or fewer.`);
      return;
    }
    this.pending.set(true);
    this.error.set('');
    try {
      const updated = await firstValueFrom(this.api.withdraw(this.dispute().id, this.dispute().version, reason));
      this.outcome.set({ kind: 'success', text: 'You withdrew this dispute. No payment adjustment was made.' });
      this.withdrawn.emit(updated);
      this.pending.set(false);
      this.closeDialog();
    } catch (failure: unknown) {
      this.pending.set(false);
      const code = problemCode(failure);
      const status = failure instanceof HttpErrorResponse ? failure.status : 0;
      if (code === 'dispute.withdrawalNotAllowed') {
        this.finishWithError('This dispute can no longer be withdrawn because it has already progressed. We refreshed the latest status.');
      } else if (code === 'dispute.concurrencyConflict') {
        this.finishWithError('This dispute changed while you were viewing it. We refreshed it - check the latest status and try again if you still want to withdraw.');
      } else if (status === 404) {
        this.finishWithError('This dispute could not be found.');
      } else if (status === 400) {
        this.error.set(`Check the reason and try again. It must be ${withdrawalReasonMaxLength} characters or fewer.`);
      } else {
        this.error.set('Unable to withdraw the dispute right now. Nothing was changed. Please try again.');
      }
    }
  }

  private finishWithError(text: string): void {
    this.outcome.set({ kind: 'error', text });
    this.refreshRequested.emit();
    this.closeDialog();
  }
}
