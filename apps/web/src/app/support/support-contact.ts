import { Component, computed, DestroyRef, inject, input, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SupportApi, SupportContact } from './support-api';

type ViewState = { kind: 'loading' } | { kind: 'error' } | { kind: 'loaded'; contact: SupportContact };

@Component({
  selector: 'cc-support-contact',
  template: `
    @let view = state();
    @if (view.kind === 'loaded') {
      <section class="support-contact" [attr.aria-labelledby]="heading() ? headingId : null"
        [attr.aria-label]="heading() ? null : 'Support contact details'">
        @if (heading()) { <h3 [id]="headingId">{{ heading() }}</h3> }
        <ul class="support-list">
          <li>Email: <a data-testid="support-email" [href]="mailto()">{{ view.contact.email }}</a></li>
          @if (view.contact.phone; as phone) {
            <li>Phone: <a data-testid="support-phone" [href]="'tel:' + dialable(phone)">{{ phone }}</a></li>
          }
          <li>Hours: {{ view.contact.hours }}</li>
          <li>Response time: {{ view.contact.responseTime }}</li>
        </ul>
        @if (reference(); as ref) { <p class="muted">Quote case reference {{ ref }} when you contact us.</p> }
      </section>
    } @else if (view.kind === 'error' && !quiet()) {
      <section class="support-contact" [attr.aria-labelledby]="heading() ? headingId : null"
        [attr.aria-label]="heading() ? null : 'Support contact details'">
        @if (heading()) { <h3 [id]="headingId">{{ heading() }}</h3> }
        <p class="muted" role="status">Support contact details are unavailable right now. Please try again shortly.</p>
        <button class="cc-button cc-button--secondary" type="button" data-testid="support-retry" (click)="load()">Retry</button>
      </section>
    }`,
})
export class SupportContactPanel implements OnInit {
  readonly heading = input('Contact support');
  readonly reference = input<string>();
  readonly quiet = input(false);
  readonly anonymous = input(false);
  private readonly api = inject(SupportApi);
  private readonly destroyRef = inject(DestroyRef);
  private static nextId = 0;
  protected readonly headingId = `support-heading-${SupportContactPanel.nextId++}`;
  protected readonly state = signal<ViewState>({ kind: 'loading' });
  protected readonly mailto = computed(() => {
    const view = this.state();
    if (view.kind !== 'loaded') return '';
    const ref = this.reference();
    const subject = ref === undefined ? 'ClearCase support request' : `ClearCase support request - ${ref}`;
    return `mailto:${view.contact.email}?subject=${encodeURIComponent(subject)}`;
  });

  ngOnInit(): void { this.load(); }

  protected load(): void {
    this.state.set({ kind: 'loading' });
    this.api.get(this.anonymous()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: contact => this.state.set({ kind: 'loaded', contact }),
      error: () => this.state.set({ kind: 'error' }),
    });
  }

  protected dialable(phone: string): string {
    return phone.replace(/[^\d+]/g, '');
  }
}
