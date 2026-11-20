import { DatePipe } from '@angular/common';
import { Component, input } from '@angular/core';
import { AgentFinancialOperation, FinancialOperationStatus } from './agent-disputes-api';
import { StatusBadge } from '../ui/ui-components';

const statusLabels: Readonly<Record<FinancialOperationStatus, string>> = {
  Pending: 'Pending', InFlight: 'In flight', Succeeded: 'Succeeded', Failed: 'Failed', Unknown: 'Unknown',
};

/** Read-only status of durable financial operations. It offers no retry; Reconcile comes only from the server's actions. */
@Component({
  selector: 'cc-operation-status',
  imports: [DatePipe, StatusBadge],
  template: `
    <section class="panel" aria-labelledby="operation-status-heading">
      <h2 id="operation-status-heading">Operation status</h2>
      <p class="muted">Read-only view of financial operations recorded for this case. No real money moves in this assessment.</p>
      @if (operations().length === 0) {
        <p class="muted" data-testid="operations-empty">No financial operations have been started for this case.</p>
      } @else {
        <ul class="operation-list" aria-label="Financial operations, oldest first">
          @for (operation of operations(); track operation.operationReference) {
            <li data-testid="operation">
              <div class="operation-head">
                <strong>{{ operation.kind }}</strong>
                <cc-status-badge [label]="label(operation.status)" [status]="operation.status" />
              </div>
              <small>Reference {{ operation.operationReference }} &middot; updated
                <time [attr.datetime]="operation.updatedAt">{{ operation.updatedAt | date:'dd MMM yyyy, HH:mm':'UTC' }} UTC</time></small>
              @if (operation.status === 'Unknown') {
                <p class="cc-form-error" data-testid="operation-unknown">The outcome is unknown. Do not retry - use Reconcile if offered.</p>
              }
            </li>
          }
        </ul>
      }
    </section>`,
})
export class OperationStatus {
  readonly operations = input.required<readonly AgentFinancialOperation[]>();
  protected label(status: FinancialOperationStatus): string {
    return statusLabels[status];
  }
}
