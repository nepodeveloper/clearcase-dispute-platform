import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { DemoEscalation } from './demo-data';
import { DemoEscalationForm } from './escalation-form';
import { DemoSession } from './demo-session';
import { DemoWorkspace } from './demo-workspace';
import { DemoHeading } from './shared';

@Component({
  selector: 'cc-demo-escalation-page',
  imports: [RouterLink, DemoHeading, DemoEscalationForm],
  template: `
    <cc-demo-heading title="Escalate a dispute" eyebrow="Agent action"
      description="Record a reason and internal note before sending the case to a supervisor." />
    @if (created(); as result) {
      <section class="panel" role="status"><h2>{{ result.reference }} created</h2>
        <p>The demo escalation is ready for specialist review. No decision or financial status has changed.</p>
        <div class="button-row">
          <a class="cc-button cc-button--secondary" [routerLink]="['/agent/disputes', id()]">Back to investigation</a>
          <button class="cc-button cc-button--primary" type="button" (click)="previewSupervisor(result.id)">Continue as supervisor &rarr;</button>
        </div>
      </section>
    } @else if (dispute(); as item) {
      @if (activeEscalation() || item.caseStatus === 'Resolved') {
        <div class="notice" role="status"><h2>Escalation unavailable</h2>
          <p>This case is resolved or already has an active escalation.</p><a [routerLink]="['/agent/disputes', id()]">Back to investigation</a></div>
      } @else {
        <div class="modal-stage"><section class="modal-card" aria-labelledby="escalation-heading">
          <cc-demo-escalation-form [caseId]="id()" (cancelled)="back()" (completed)="created.set($event)" />
        </section></div>
      }
    } @else {
      <div class="cc-empty"><h2>Case not found</h2><a routerLink="/agent/disputes">Back to agent queue</a></div>
    }`,
})
export class DemoEscalationPage {
  readonly id = input.required<string>();
  private readonly workspace = inject(DemoWorkspace);
  private readonly router = inject(Router);
  private readonly session = inject(DemoSession);
  protected readonly dispute = computed(() => this.workspace.agentCase(this.id()));
  protected readonly activeEscalation = computed(() => this.workspace.activeEscalation(this.id()));
  protected readonly created = signal<DemoEscalation | null>(null);
  constructor() {
    effect(() => { this.id(); this.created.set(null); });
  }
  protected back(): void { void this.router.navigate(['/agent/disputes', this.id()]); }
  protected previewSupervisor(id: string): void {
    this.session.enter('supervisor');
    void this.router.navigate(['/agent/escalations', id]);
  }
}
