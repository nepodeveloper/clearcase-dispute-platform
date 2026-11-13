import { DatePipe } from '@angular/common';
import { afterNextRender, Component, computed, effect, ElementRef, inject, Injector, input, signal, viewChild } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { DemoEscalation } from './demo-data';
import { DemoEscalationForm } from './escalation-form';
import { DemoSession } from './demo-session';
import { DemoWorkspace } from './demo-workspace';
import { DemoHeading, DemoMoney, DemoStatus, transactionTypeLabel } from './shared';

@Component({
  selector: 'cc-demo-investigation',
  imports: [DatePipe, ReactiveFormsModule, RouterLink, DemoHeading, DemoMoney, DemoStatus, DemoEscalationForm],
  template: `
    @if (dispute(); as item) {
      <cc-demo-heading title="Case investigation" [description]="item.reference + ' \u00b7 ' + item.customerName + ' \u00b7 ' + typeLabel(item.type)">
        <a back class="back" routerLink="/agent/disputes">&larr; Back to agent queue</a>
        <button #escalateButton class="cc-button cc-button--primary" type="button" (click)="openDialog()"
          [disabled]="item.caseStatus === 'Resolved' || activeEscalation() !== undefined"><span aria-hidden="true">&nearr;</span> Escalate case</button>
      </cc-demo-heading>
      @if (confirmation()) { <p class="notice" role="status">{{ confirmation() }}</p> }
      <div class="split-grid unequal">
        <div class="stack">
          <section class="panel"><div class="panel-title"><h2>Dispute information</h2><cc-demo-status [label]="item.caseStatus" /></div>
            <dl class="description-grid">
              <dt>Merchant</dt><dd>{{ item.merchant }}</dd><dt>Amount</dt><dd>{{ item.amountMinor | demoMoney }}</dd>
              <dt>Customer</dt><dd>{{ item.customerName }}</dd><dt>Reason</dt><dd>{{ item.reason }}</dd>
              <dt>Decision</dt><dd>{{ item.decisionStatus }}</dd><dt>Financial status</dt><dd>{{ item.financialStatus }}</dd>
            </dl>
            <div class="soft-box"><small>Customer statement</small><p>{{ item.statement }}</p></div>
          </section>
          <section class="panel"><h2>Investigation notes</h2>
            <p class="muted">Internal operational notes are private and never displayed in customer views.</p>
            <div class="soft-box"><small>Agent review</small><p>Further verification required before an outcome can be determined.</p></div>
            @for (note of notes(); track note.id) {
              <div class="soft-box"><small>{{ note.occurredAt | date:'dd MMM yyyy, HH:mm':'UTC' }} UTC &middot; {{ note.author }}</small><p>{{ note.text }}</p></div>
            }
            <form class="form-stack note-form" (submit)="addNote($event)" novalidate>
              <div class="cc-field"><label for="agent-note">Add an internal note</label>
                <textarea id="agent-note" class="cc-control" rows="3" [formControl]="note" maxlength="2000"
                  aria-describedby="agent-note-hint agent-note-error" [attr.aria-invalid]="noteError() !== ''"></textarea>
                <span class="field-hint" id="agent-note-hint">15&ndash;2000 characters. Use synthetic information only.</span>
              </div>
              <div id="agent-note-error">@if (noteError()) { <p class="cc-form-error" role="alert">{{ noteError() }}</p> }</div>
              <button class="cc-button cc-button--secondary" type="submit">Save demo note</button>
            </form>
          </section>
        </div>
        <div class="stack">
          <section class="panel"><h2>Case milestones</h2><ul class="mini-timeline">
            @for (event of item.timeline; track event.id) {
              <li><span class="dot ok" aria-hidden="true"></span><span>{{ event.title }} &middot; {{ event.occurredAt | date:'dd MMM':'UTC' }}</span></li>
            }
          </ul></section>
          <aside class="panel info-panel"><h3>Next best action</h3>
            @if (activeEscalation(); as escalation) {
              <p>{{ escalation.reference }} &middot; {{ escalation.status }}. This case has already been referred for specialist review.</p>
              <button class="cc-button cc-button--secondary full" type="button" (click)="previewSupervisor(escalation.id)">Preview supervisor review &rarr;</button>
            } @else if (item.caseStatus === 'Resolved') {
              <p>This case is resolved. No new escalation is available.</p>
            } @else {
              <p>Refer this case for specialist review if normal investigation cannot establish transaction authorization.</p>
              <button class="cc-button cc-button--primary full" type="button" (click)="openDialog()">Escalate to supervisor &rarr;</button>
            }
          </aside>
        </div>
      </div>
      <dialog #escalationDialog class="demo-dialog" aria-labelledby="escalation-heading"
        (keydown)="dialogKeydown($event)" (close)="dialogClosed()">
        @if (dialogOpen()) {
          <cc-demo-escalation-form [caseId]="id()" (cancelled)="closeDialog()" (completed)="escalated($event)" />
        }
      </dialog>
    } @else {
      <div class="cc-empty"><h1 tabindex="-1">Case not found</h1><a routerLink="/agent/disputes">Back to agent queue</a></div>
    }`,
})
export class DemoInvestigationPage {
  readonly id = input.required<string>();
  private readonly workspace = inject(DemoWorkspace);
  private readonly session = inject(DemoSession);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly dialog = viewChild<ElementRef<HTMLDialogElement>>('escalationDialog');
  private opener: HTMLElement | null = null;
  protected readonly dispute = computed(() => this.workspace.agentCase(this.id()));
  protected readonly activeEscalation = computed(() => this.workspace.activeEscalation(this.id()));
  protected readonly notes = computed(() => this.workspace.notes(this.id()));
  protected readonly typeLabel = transactionTypeLabel;
  protected readonly dialogOpen = signal(false);
  protected readonly confirmation = signal('');
  protected readonly noteError = signal('');
  protected readonly note = new FormControl('', { nonNullable: true });

  constructor() {
    effect(() => {
      this.id();
      this.note.reset();
      this.noteError.set('');
      this.confirmation.set('');
    });
  }

  protected openDialog(): void {
    this.opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    this.dialogOpen.set(true);
    afterNextRender(() => this.dialog()?.nativeElement.showModal(), { injector: this.injector });
  }

  protected closeDialog(): void { this.dialog()?.nativeElement.close(); }

  protected dialogKeydown(event: KeyboardEvent): void {
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
    if (this.opener instanceof HTMLButtonElement && this.opener.disabled) {
      document.querySelector<HTMLElement>('main h1')?.focus();
    } else {
      this.opener?.focus();
    }
  }

  protected escalated(result: DemoEscalation): void {
    this.confirmation.set(`${result.reference} created in memory. The supervisor can now review it. No money has moved.`);
    this.closeDialog();
  }

  protected addNote(event: Event): void {
    event.preventDefault();
    const result = this.workspace.addNote(this.id(), this.note.value);
    if (!result.ok) { this.noteError.set(result.message); return; }
    this.note.reset();
    this.noteError.set('');
    this.confirmation.set('Internal demo note saved. It is not visible in the customer timeline.');
  }

  protected previewSupervisor(id: string): void {
    this.session.enter('supervisor');
    void this.router.navigate(['/agent/escalations', id]);
  }
}
