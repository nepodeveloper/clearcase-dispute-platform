import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { DISPUTE_REASONS } from './demo-data';
import { DemoWorkspace } from './demo-workspace';
import { DemoHeading, DemoMoney, DemoStatus, transactionTypeLabel } from './shared';

@Component({
  selector: 'cc-demo-transaction',
  imports: [DatePipe, ReactiveFormsModule, RouterLink, DemoHeading, DemoMoney, DemoStatus],
  template: `
    @if (transaction(); as item) {
      <cc-demo-heading title="Transaction details" description="Review the details and tell us what happened.">
        <a back class="back" routerLink="/" data-testid="back-to-transactions">&larr; Back to transactions</a>
        <cc-demo-status label="Posted" />
      </cc-demo-heading>
      <div class="split-grid">
        <div class="stack">
          <section class="panel" aria-label="Transaction information">
            <div class="transaction-hero">
              <span class="glyph" aria-hidden="true">&#9635;</span>
              <div><strong>{{ item.description }}</strong><small>{{ typeLabel(item.type) }} &middot; {{ item.occurredAt | date:'dd MMM yyyy, HH:mm':'UTC' }} UTC</small></div>
              <div class="transaction-amount">&minus;{{ item.amountMinor | demoMoney }}</div>
            </div>
            <dl class="description-grid">
              <dt>Reference</dt><dd>{{ item.reference }}</dd>
              <dt>Transaction type</dt><dd>{{ typeLabel(item.type) }}</dd>
              <dt>Account</dt><dd>Main account &bull;&bull;&bull;&bull; 4821</dd>
              <dt>Currency</dt><dd>ZAR</dd>
            </dl>
          </section>
          <aside class="panel info-panel"><h3>What happens next?</h3>
            <p>We will review the information, investigate with the relevant team and keep the case timeline updated.
              Submitting a dispute does not immediately reverse the transaction.</p>
          </aside>
        </div>
        <section class="panel" aria-labelledby="submit-heading">
          <div class="panel-title"><div><cc-demo-status label="Demo review available" />
            <h2 id="submit-heading" class="spaced-title">Submit a dispute</h2>
            <p>Tell us what doesn't look right with this transaction.</p></div></div>
          @if (activeCase(); as existing) {
            <div class="notice" role="status">
              <h3>A dispute is already in progress</h3><p>{{ existing.reference }} is being reviewed.</p>
              <a class="cc-button cc-button--secondary" [routerLink]="['/disputes', existing.id]">View existing timeline &rarr;</a>
            </div>
          } @else {
            <form class="form-stack" [formGroup]="form" (ngSubmit)="submit()" novalidate>
              <div class="cc-field"><label for="dispute-reason">Dispute reason</label>
                <select class="cc-control" id="dispute-reason" formControlName="reason">
                  @for (reason of reasons; track reason) { <option [value]="reason">{{ reason }}</option> }
                </select></div>
              <div class="cc-field"><label for="dispute-description">Describe the issue</label>
                <textarea class="cc-control" id="dispute-description" formControlName="description" rows="6" maxlength="2000"
                  placeholder="Tell us what happened using synthetic information only."
                  [attr.aria-invalid]="error() !== ''" aria-describedby="dispute-hint dispute-error"></textarea>
                <span class="field-hint" id="dispute-hint">15&ndash;2000 characters &middot; Avoid passwords, PINs or personal information.</span>
              </div>
              <div id="dispute-error">
                @if (error()) { <p class="cc-form-error" role="alert">{{ error() }}</p> }
              </div>
              <div class="button-row align-right"><a class="cc-button cc-button--secondary" routerLink="/">Cancel</a>
                <button class="cc-button cc-button--primary" type="submit" data-testid="submit-dispute">Submit demo dispute &rarr;</button>
              </div>
              <p class="field-hint">UI simulation only. Eligibility policy and API validation are not connected.</p>
            </form>
          }
        </section>
      </div>
    } @else {
      <div class="cc-empty"><h1 tabindex="-1">Transaction not found</h1><p>This demo transaction is not available.</p><a routerLink="/">Back to transactions</a></div>
    }`,
})
export class DemoTransactionPage {
  readonly id = input.required<string>();
  private readonly workspace = inject(DemoWorkspace);
  private readonly router = inject(Router);
  protected readonly typeLabel = transactionTypeLabel;
  protected readonly reasons = DISPUTE_REASONS;
  protected readonly transaction = computed(() => this.workspace.transaction(this.id()));
  protected readonly activeCase = computed(() => this.workspace.activeDispute(this.id()));
  protected readonly error = signal('');
  protected readonly form = new FormGroup({
    reason: new FormControl<string>(DISPUTE_REASONS[0], { nonNullable: true, validators: Validators.required }),
    description: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(15), Validators.maxLength(2000)] }),
  });

  constructor() {
    effect(() => {
      this.id();
      this.form.reset({ reason: DISPUTE_REASONS[0], description: '' });
      this.error.set('');
    });
  }

  protected submit(): void {
    const { reason, description } = this.form.getRawValue();
    const result = this.workspace.submitDispute(this.id(), reason, description);
    if (!result.ok) { this.error.set(result.message); return; }
    this.error.set('');
    void this.router.navigate(['/disputes', result.value.id]);
  }
}
