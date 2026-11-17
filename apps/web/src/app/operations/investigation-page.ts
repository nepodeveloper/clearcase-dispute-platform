import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { afterNextRender, Component, computed, effect, ElementRef, inject, Injector, input, signal, viewChild } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { transactionTypeLabel } from '../transactions/transaction-type-label';
import { MinorUnitsMoney, PageHeading, StatusBadge } from '../ui/ui-components';
import { AgentEscalationCreation, agentCaseStatusLabel, AgentDisputeDetails, AgentDisputesApi } from './agent-disputes-api';
import { EscalationForm } from './escalation-form';
import { StaffCommandPanel } from './staff-command-panel';

@Component({
  selector: 'cc-investigation',
  imports: [DatePipe, ReactiveFormsModule, RouterLink, MinorUnitsMoney, PageHeading, StatusBadge, EscalationForm, StaffCommandPanel],
  template: `
    @if (loading() && dispute() === null) {
      <p class="notice" role="status">Loading persisted investigation...</p>
    } @else if (loadError()) {
      <div class="cc-empty" role="alert"><h1 tabindex="-1">Investigation unavailable</h1><p>{{ loadError() }}</p>
        <button class="cc-button cc-button--secondary" type="button" (click)="loadDispute()">Retry</button>
      </div>
    } @else if (dispute(); as item) {
      <cc-page-heading title="Case investigation"
        [description]="item.reference + ' \u00b7 ' + item.customerReference + ' \u00b7 ' + typeLabel(item.category)">
        <a back class="back" routerLink="/agent/disputes">&larr; Back to agent queue</a>
        <button class="cc-button cc-button--primary" type="button" (click)="openDialog()"
          [disabled]="!canEscalate(item)"><span aria-hidden="true">&nearr;</span> Escalate case</button>
      </cc-page-heading>
      @if (confirmation()) { <p class="notice" role="status">{{ confirmation() }}</p> }
      <div class="split-grid unequal">
        <div class="stack">
          <section class="panel"><div class="panel-title"><h2>Dispute information</h2>
            <cc-status-badge [label]="statusLabel(item.caseStatus)" [status]="item.caseStatus" /></div>
            <dl class="description-grid">
              <dt>Transaction</dt><dd>{{ item.transaction.description }}</dd>
              <dt>Amount</dt><dd>{{ item.transaction.amountMinorUnits | minorUnitsMoney }} {{ item.transaction.currency }}</dd>
              <dt>Customer reference</dt><dd>{{ item.customerReference }}</dd><dt>Reason</dt><dd>{{ item.reasonCode }}</dd>
              <dt>Decision</dt><dd>{{ item.decisionStatus }}</dd><dt>Financial status</dt><dd>{{ item.financialStatus }}</dd>
              <dt>Eligibility</dt><dd>{{ item.eligibilityOutcome }} &middot; {{ item.eligibilityPolicyVersion }}</dd>
              @if (item.transaction.mandateType) { <dt>Mandate type</dt><dd>{{ item.transaction.mandateType }}</dd> }
              @if (item.transaction.collectionDate) { <dt>Collection date</dt><dd>{{ item.transaction.collectionDate }}</dd> }
              @if (item.transaction.mandateAuthorised !== undefined) {
                <dt>Mandate authorized</dt><dd>{{ item.transaction.mandateAuthorised ? 'Yes' : 'No' }}</dd>
              }
              @if (item.transaction.mandateMatchesCollection !== undefined) {
                <dt>Mandate matches collection</dt><dd>{{ item.transaction.mandateMatchesCollection ? 'Yes' : 'No' }}</dd>
              }
            </dl>
            <div class="soft-box"><small>Customer statement</small><p>{{ item.customerStatement }}</p></div>
          </section>
          <section class="panel private-notes"><h2>Investigation notes</h2>
            <p class="muted">Internal operational notes are private and never displayed in customer views.</p>
            @for (entry of item.notes; track entry.id) {
              <div class="soft-box"><small>{{ entry.createdAt | date:'dd MMM yyyy, HH:mm':'UTC' }} UTC &middot; Agent</small><p>{{ entry.text }}</p></div>
            }
            <form class="form-stack note-form" (submit)="addNote($event)" novalidate>
              <div class="cc-field"><label for="agent-note">Add an internal note</label>
                <textarea id="agent-note" class="cc-control" rows="3" [formControl]="note" maxlength="2000"
                  [disabled]="notePending()"
                  aria-describedby="agent-note-hint agent-note-error" [attr.aria-invalid]="noteError() !== ''"></textarea>
                <span class="field-hint" id="agent-note-hint">15&ndash;2000 characters. Use synthetic information only.</span>
              </div>
              <div id="agent-note-error">@if (noteError()) { <p class="cc-form-error" role="alert">{{ noteError() }}</p> }</div>
              <button class="cc-button cc-button--secondary" type="submit" [disabled]="notePending()">
                {{ notePending() ? 'Saving note...' : 'Save internal note' }}</button>
            </form>
          </section>
        </div>
        <div class="stack">
          <section class="panel"><h2>Case milestones</h2><ul class="mini-timeline" aria-label="Customer-visible case timeline">
            @for (event of item.timeline; track event.id) {
              <li><span class="dot" aria-hidden="true"></span><span>{{ event.eventType }} &middot; {{ event.occurredAt | date:'dd MMM':'UTC' }}</span></li>
            }
          </ul></section>
          <aside class="panel info-panel"><h3>Next best action</h3>
            @if (item.activeEscalation; as escalation) {
              <p>{{ escalation.reference }} &middot; {{ escalation.status }}. This case has already been referred for specialist review.</p>
            } @else if (!canEscalate(item)) {
              <p>This case is not eligible for escalation in its current state.</p>
            } @else {
              <p>Refer this case for specialist review if normal investigation cannot establish transaction authorization.</p>
              <button class="cc-button cc-button--primary full" type="button" (click)="openDialog()">Escalate to supervisor &rarr;</button>
            }
          </aside>
        </div>
      </div>
      <dialog #escalationDialog class="cc-dialog" aria-labelledby="escalation-heading"
        (keydown)="dialogKeydown($event)" (close)="dialogClosed()">
        @if (dialogOpen()) {
          <cc-escalation-form [caseId]="id()" [reference]="item.reference"
            (cancelled)="closeDialog()" (completed)="escalated($event)" />
        }
      </dialog>
    }`,
})
export class InvestigationPage {
  readonly id = input.required<string>();
  private readonly api = inject(AgentDisputesApi);
  private readonly injector = inject(Injector);
  private readonly dialog = viewChild<ElementRef<HTMLDialogElement>>('escalationDialog');
  private opener: HTMLElement | null = null;
  protected readonly dispute = signal<AgentDisputeDetails | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal('');
  protected readonly notePending = signal(false);
  private noteRequest: { key: string; text: string } | null = null;
  protected readonly typeLabel = transactionTypeLabel;
  protected readonly statusLabel = agentCaseStatusLabel;
  protected readonly dialogOpen = signal(false);
  protected readonly confirmation = signal('');
  protected readonly noteError = signal('');
  protected readonly note = new FormControl('', { nonNullable: true });

  constructor() {
    effect(() => {
      const id = this.id();
      this.note.reset();
      this.noteError.set('');
      this.confirmation.set('');
      this.noteRequest = null;
      this.dispute.set(null);
      void this.loadDispute(id);
    });
  }

  protected async loadDispute(id = this.id()): Promise<void> {
    this.loading.set(true);
    this.loadError.set('');
    try {
      this.dispute.set(await firstValueFrom(this.api.get(id)));
    } catch (error: unknown) {
      this.loadError.set(error instanceof HttpErrorResponse && error.status === 404
        ? 'This dispute could not be found in the operational scope.'
        : 'Unable to load the investigation. Check your bank employee session and retry.');
    } finally {
      this.loading.set(false);
    }
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

  protected escalated(result: AgentEscalationCreation): void {
    this.confirmation.set(`${result.reference} saved. The supervisor can now review it. No money has moved.`);
    this.closeDialog();
    void this.loadDispute();
  }

  protected async addNote(event: Event): Promise<void> {
    event.preventDefault();
    if (this.notePending()) return;
    const text = this.note.value.trim().normalize('NFC');
    if (text.length < 15 || text.length > 2000) {
      this.noteError.set('Enter an internal note between 15 and 2000 normalized characters.');
      return;
    }
    // Retrying the same text reuses its key so the server replays instead of duplicating.
    let request = this.noteRequest;
    if (request?.text !== text) {
      request = { key: crypto.randomUUID(), text };
      this.noteRequest = request;
    }
    this.notePending.set(true);
    try {
      await firstValueFrom(this.api.createNote(this.id(), text, request.key));
      this.noteRequest = null;
      this.note.reset();
      this.noteError.set('');
      this.confirmation.set('Internal investigation note saved. It is not visible in the customer timeline.');
      await this.loadDispute();
    } catch {
      this.noteError.set('Unable to save the note. Retry with the same text.');
    } finally {
      this.notePending.set(false);
    }
  }

  protected canEscalate(dispute: AgentDisputeDetails): boolean {
    return dispute.activeEscalation === undefined
      && dispute.decisionStatus === 'Pending'
      && dispute.financialStatus === 'NotInitiated'
      && !['Closed', 'Rejected', 'ReversalConfirmed'].includes(dispute.caseStatus);
  }
}
