import { DatePipe } from '@angular/common';
import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CustomerEvidence } from '../disputes/dispute-api';
import { formatFileSize } from '../disputes/dispute-evidence-panel';
import { AgentDisputesApi } from './agent-disputes-api';

type ListState = { kind: 'loading' } | { kind: 'error' } | { kind: 'loaded'; items: CustomerEvidence[] };

/** Read-only list of the documents the customer uploaded for a case, with authenticated downloads. */
@Component({
  selector: 'cc-staff-evidence',
  imports: [DatePipe],
  template: `
    <section class="panel" aria-labelledby="staff-evidence-heading">
      <h2 id="staff-evidence-heading">Customer evidence</h2>
      <p class="muted">Read-only. Documents were scanned on upload; open them only in a trusted viewer.</p>
      @let view = state();
      @if (view.kind === 'loading') {
        <p class="muted" role="status">Loading customer evidence...</p>
      } @else if (view.kind === 'error') {
        <div class="state-message" role="alert"><p>Unable to load customer evidence.</p>
          <button class="cc-button cc-button--secondary" type="button" data-testid="evidence-retry" (click)="load()">Retry</button></div>
      } @else if (view.items.length === 0) {
        <p class="muted" data-testid="evidence-empty">The customer has not uploaded any documents.</p>
      } @else {
        <ul class="evidence-list" aria-label="Customer evidence">
          @for (item of view.items; track item.id) {
            <li>
              <div>
                <strong>{{ item.fileName }}</strong>
                <small>{{ sizeLabel(item.sizeBytes) }} &middot; {{ item.contentType }} &middot; uploaded
                  <time [attr.datetime]="item.uploadedAt">{{ item.uploadedAt | date:'dd MMM yyyy, HH:mm':'UTC' }} UTC</time></small>
                @if (item.note) { <small>Customer note: {{ item.note }}</small> }
              </div>
              <a class="cc-button cc-button--secondary" data-testid="evidence-download" [href]="contentUrl(item)"
                [attr.download]="item.fileName" [attr.aria-label]="'Download ' + item.fileName">Download</a>
            </li>
          }
        </ul>
      }
    </section>`,
})
export class StaffEvidence {
  readonly disputeId = input.required<string>();
  private readonly api = inject(AgentDisputesApi);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly state = signal<ListState>({ kind: 'loading' });
  protected readonly sizeLabel = formatFileSize;

  constructor() {
    effect(() => {
      this.disputeId();
      untracked(() => this.load());
    });
  }

  protected load(): void {
    this.state.set({ kind: 'loading' });
    this.api.listEvidence(this.disputeId()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => this.state.set({ kind: 'loaded', items: result.items }),
      error: () => this.state.set({ kind: 'error' }),
    });
  }

  protected contentUrl(item: CustomerEvidence): string {
    return this.api.evidenceContentUrl(this.disputeId(), item.id);
  }
}
