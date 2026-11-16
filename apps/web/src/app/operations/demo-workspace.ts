import { computed, inject, Injectable, signal } from '@angular/core';
import {
  AgentCase, DEMO_DATE, DEMO_TRANSACTIONS, DemoEscalation, DemoEvent, DemoNote,
  DISPUTE_REASONS, ESCALATION_REASONS, PublicCase, demoId, initialCases, initialEscalations,
} from './demo-data';
import { DemoSession } from './demo-session';

export type DemoResult<T> = { ok: true; value: T } | { ok: false; message: string };

@Injectable()
export class DemoWorkspace {
  private readonly session = inject(DemoSession);
  private readonly cases = signal(initialCases());
  private readonly escalationState = signal(initialEscalations());
  private readonly noteState = signal<Readonly<Record<string, readonly DemoNote[]>>>({});
  private sequence = 600;
  readonly transactions = DEMO_TRANSACTIONS;
  readonly agentCases = this.cases.asReadonly();
  readonly escalations = this.escalationState.asReadonly();
  readonly customerCases = computed(() => this.cases()
    .filter(item => item.customerKey === 'demo-customer')
    .map(({ customerKey, customerName, ...item }): PublicCase => item)
    .sort((a, b) => b.updatedAt.localeCompare(a.updatedAt) || a.id.localeCompare(b.id)));

  transaction(id: string) { return this.transactions.find(item => item.id === id); }
  customerCase(id: string) { return this.customerCases().find(item => item.id === id); }
  agentCase(id: string) { return this.cases().find(item => item.id === id); }
  escalation(id: string) { return this.escalations().find(item => item.id === id); }
  notes(id: string) { return this.noteState()[id] ?? []; }
  activeDispute(transactionId: string) {
    return this.customerCases().find(item =>
      item.transactionId === transactionId && item.caseStatus !== 'Resolved');
  }
  activeEscalation(caseId: string) {
    return this.escalations().find(item => item.caseId === caseId && item.status !== 'Resolved');
  }

  submitDispute(transactionId: string, reason: string, statement: string): DemoResult<PublicCase> {
    if (this.session.role() !== 'customer') return this.failure('Choose the customer demo journey first.');
    const transaction = this.transaction(transactionId);
    if (transaction === undefined) return this.failure('Transaction not found.');
    if (this.activeDispute(transactionId) !== undefined)
      return this.failure('An active dispute already exists for this transaction. View its timeline instead.');
    if (!DISPUTE_REASONS.some(item => item === reason) || !this.validNote(statement))
      return this.failure('Choose a reason and enter a description between 15 and 2000 characters.');
    const event = this.event('Dispute submitted', 'Your demo dispute was received. No money has moved.');
    const item: AgentCase = {
      id: demoId(++this.sequence), reference: `CC-2026-${this.sequence.toString().padStart(5, '0')}`,
      transactionId, merchant: transaction.description, type: transaction.type,
      amountMinor: transaction.amountMinor, reason, statement: statement.trim(),
      customerKey: 'demo-customer', customerName: 'T. Mokone',
      caseStatus: 'Submitted', decisionStatus: 'Pending', financialStatus: 'Not initiated',
      createdAt: event.occurredAt, updatedAt: event.occurredAt, timeline: [event],
    };
    this.cases.update(items => [item, ...items]);
    const { customerKey, customerName, ...publicCase } = item;
    return { ok: true, value: publicCase };
  }

  addNote(caseId: string, text: string): DemoResult<DemoNote> {
    if (this.session.role() !== 'agent') return this.failure('Only the agent demo journey can add notes.');
    if (this.agentCase(caseId) === undefined) return this.failure('Case not found.');
    if (!this.validNote(text)) return this.failure('Enter an internal note between 15 and 2000 characters.');
    const event = this.event('Agent note', text.trim());
    const note: DemoNote = { id: event.id, author: 'Agent', text: text.trim(), occurredAt: event.occurredAt };
    this.noteState.update(items => ({ ...items, [caseId]: [...(items[caseId] ?? []), note] }));
    return { ok: true, value: note };
  }

  escalate(caseId: string, reason: string, note: string): DemoResult<DemoEscalation> {
    if (this.session.role() !== 'agent') return this.failure('Only the agent demo journey can escalate cases.');
    const item = this.agentCase(caseId);
    if (item === undefined) return this.failure('Case not found.');
    if (item.caseStatus === 'Resolved') return this.failure('A resolved case cannot be escalated.');
    if (this.activeEscalation(caseId) !== undefined) return this.failure('This case already has an active escalation.');
    if (!ESCALATION_REASONS.some(value => value === reason) || !this.validNote(note))
      return this.failure('Choose a reason and enter an internal note between 15 and 2000 characters.');
    const event = this.event('Escalation created', 'Agent requested specialist review.');
    const escalation: DemoEscalation = {
      id: demoId(++this.sequence), reference: `ESC-${this.sequence.toString().padStart(6, '0')}`,
      caseId, reason, note: note.trim(), status: 'Open', createdAt: event.occurredAt, history: [event],
    };
    this.escalationState.update(items => [escalation, ...items]);
    this.updateCase(caseId, 'Specialist review',
      this.event('Specialist review', 'Your case has been referred for additional review.'));
    return { ok: true, value: escalation };
  }

  acknowledge(id: string): DemoResult<DemoEscalation> {
    if (this.session.role() !== 'supervisor') return this.failure('Choose the supervisor demo journey first.');
    const item = this.escalation(id);
    if (item === undefined) return this.failure('Escalation not found.');
    if (item.status !== 'Open') return this.failure('Only an open escalation can be acknowledged.');
    return this.updateEscalation(item, 'Acknowledged',
      this.event('Supervisor acknowledgement', 'Supervisor is reviewing this escalation.'));
  }

  resolve(id: string, note: string): DemoResult<DemoEscalation> {
    if (this.session.role() !== 'supervisor') return this.failure('Choose the supervisor demo journey first.');
    const item = this.escalation(id);
    if (item === undefined) return this.failure('Escalation not found.');
    if (item.status !== 'Acknowledged') return this.failure('Acknowledge the escalation before resolving it.');
    if (!this.validNote(note)) return this.failure('Enter a resolution note between 15 and 2000 characters.');
    const result = this.updateEscalation(item, 'Resolved', this.event('Resolution recorded', note.trim()));
    this.updateCase(item.caseId, 'Under review', this.event('Specialist review complete',
      'Additional review is complete. The investigation continues; no refund is confirmed.'));
    return result;
  }

  private updateEscalation(item: DemoEscalation, status: DemoEscalation['status'], event: DemoEvent) {
    const updated = { ...item, status, history: [...item.history, event] };
    this.escalationState.update(items => items.map(value => value.id === item.id ? updated : value));
    return { ok: true, value: updated } as const;
  }

  private updateCase(id: string, caseStatus: AgentCase['caseStatus'], event: DemoEvent): void {
    this.cases.update(items => items.map(item => item.id === id
      ? { ...item, caseStatus, updatedAt: event.occurredAt, timeline: [...item.timeline, event] } : item));
  }

  private event(title: string, description: string): DemoEvent {
    const sequence = ++this.sequence;
    return { id: demoId(sequence), title, description,
      occurredAt: new Date(Date.parse(DEMO_DATE) + sequence * 1000).toISOString() };
  }

  private validNote(value: string): boolean {
    return value.trim().length >= 15 && value.trim().length <= 2000;
  }

  private failure(message: string): DemoResult<never> { return { ok: false, message }; }
}
