import { DatePipe } from '@angular/common';
import { Component, computed, DestroyRef, ElementRef, HostListener, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { finalize, firstValueFrom } from 'rxjs';
import { AuthService } from '../auth/auth-service';
import { transactionTypeLabel } from '../transactions/transaction-type-label';
import { createFilterDebouncer } from '../ui/filter-debounce';
import { defaultPageSize, MinorUnitsMoney, PageHeading, PageSize, PageSizeSelect, StatCard, StatusBadge } from '../ui/ui-components';
import { assignmentFailureReason } from './assignment-errors';
import {
  AgentCaseStatus,
  agentCaseStatusLabel,
  AgentDisputeCategory,
  AgentDisputeFilters,
  AgentDisputeListItem,
  AgentDisputePage,
  AgentDisputesApi,
  AgentDisputeDetails,
  AssignmentFilter,
  slaStatusLabel,
} from './agent-disputes-api';
import {
  maxPresetNameLength, QueueFilterPreset, readQueueFilterPresets, upsertPreset, writeQueueFilterPresets,
} from './queue-filter-presets';
import { hasNativeEnterAction, shortcutsActive } from './queue-shortcuts';

const caseStatuses: readonly AgentCaseStatus[] = ['Submitted', 'ManualReview', 'UnderReview', 'MandateVerified',
  'ReversalRequested', 'ReversalConfirmed', 'Closed', 'Rejected'];
const categories: readonly AgentDisputeCategory[] = ['CardPurchase', 'DebitOrder', 'BankCharge'];
const assignmentOptions: readonly { value: AssignmentFilter; label: string }[] = [
  { value: 'all', label: 'All cases' }, { value: 'mine', label: 'Assigned to me' }, { value: 'unassigned', label: 'Unassigned' }];
const finishedStatuses: readonly AgentCaseStatus[] = ['Closed', 'Rejected', 'ReversalConfirmed'];

interface BulkResult { total: number; succeeded: number; failures: { reference: string; reason: string }[] }

@Component({
  selector: 'cc-agent-queue',
  imports: [DatePipe, RouterLink, MinorUnitsMoney, PageHeading, PageSizeSelect, StatCard, StatusBadge],
  template: `
    <cc-page-heading title="Dispute work queue" eyebrow="Operations workspace"
      description="Review incoming cases, prioritise investigations and refer complex issues.">
      <span class="info-token">AG &middot; Agent</span>
    </cc-page-heading>
    <div class="stats-grid">
      <cc-stat label="Open cases" [value]="open()" hint="In current results" />
      <cc-stat label="Needs review" [value]="needsReview()" hint="In current results" />
      <cc-stat label="Escalated" [value]="escalated()" hint="In current results" />
    </div>
    <section class="panel" aria-labelledby="queue-heading" [attr.aria-busy]="loading()">
      <div class="panel-title"><h2 id="queue-heading">Assigned case queue</h2>
        <span class="muted">Persisted operational cases</span></div>
      <div class="filter-bar operations-filters">
        <label class="cc-field search-field"><span>Search cases</span>
          <input class="cc-control" type="search" #query placeholder="Search case reference or customer"
            [value]="search()" (input)="searchTyped(query.value)" (keydown.enter)="applyNow()"></label>
        <label class="cc-field"><span>Case status</span>
          <select class="cc-control" #statusFilter (change)="setStatus(statusFilter.value)">
            <option value="" [selected]="status() === ''">All statuses</option>
            @for (option of statuses; track option) {
              <option [value]="option" [selected]="status() === option">{{ statusLabel(option) }}</option> }
          </select></label>
        <label class="cc-field"><span>Dispute type</span>
          <select class="cc-control" #type (change)="setCategory(type.value)">
            <option value="" [selected]="category() === ''">All types</option>
            @for (option of categories; track option) {
              <option [value]="option" [selected]="category() === option">{{ typeLabel(option) }}</option> }
          </select></label>
        <label class="cc-field"><span>Assignment</span>
          <select class="cc-control" data-testid="assignment-filter" #assignmentFilter (change)="setAssignment(assignmentFilter.value)">
            @for (option of assignmentOptions; track option.value) {
              <option [value]="option.value" [selected]="assignment() === option.value">{{ option.label }}</option> }
          </select></label>
      </div>
      <p class="muted filter-hint">Results update as you type or choose a filter.</p>
      <details class="queue-presets">
        <summary>Saved filters</summary>
        <div class="filter-bar">
          <label class="cc-field"><span>Saved filter</span>
            <select class="cc-control" data-testid="preset-select" #presetSelect (change)="presetSelected.set(presetSelect.value)">
              <option value="" [selected]="presetSelected() === ''">Choose a saved filter</option>
              @for (preset of presets(); track preset.name) {
                <option [value]="preset.name" [selected]="presetSelected() === preset.name">{{ preset.name }}</option> }
            </select></label>
          <button class="cc-button cc-button--secondary" type="button" data-testid="preset-apply"
            [disabled]="presetSelected() === ''" (click)="applyPreset()">Apply</button>
          <button class="cc-button cc-button--secondary" type="button" data-testid="preset-delete"
            [disabled]="presetSelected() === ''" (click)="deletePreset()">Delete</button>
          <label class="cc-field"><span>Name for the current filters</span>
            <input class="cc-control" type="text" data-testid="preset-name" #presetField [maxLength]="presetNameLimit"
              [value]="presetName()" (input)="presetName.set(presetField.value)"></label>
          <button class="cc-button cc-button--secondary" type="button" data-testid="preset-save" (click)="savePreset()">Save filters</button>
        </div>
        <p class="muted">Saved in this browser only. Only the search text and filter choices are stored.</p>
        <p class="muted" role="status" data-testid="preset-message">{{ presetMessage() }}</p>
      </details>
      <details class="shortcut-help">
        <summary>Keyboard shortcuts</summary>
        <dl class="shortcut-list">
          <dt><kbd>/</kbd></dt><dd>Focus the search box</dd>
          <dt><kbd>j</kbd> / <kbd>k</kbd></dt><dd>Move to the next / previous case</dd>
          <dt><kbd>Enter</kbd></dt><dd>Open the selected case</dd>
          <dt><kbd>m</kbd></dt><dd>Toggle &ldquo;Assigned to me&rdquo;</dd>
        </dl>
        <p class="muted">Shortcuts are off while you type in a field and when Ctrl, Alt, Shift or Cmd is held.</p>
      </details>
      @if (error()) {
        <div class="cc-form-error" role="alert"><p>{{ error() }}</p>
          <button class="cc-button cc-button--secondary" type="button" (click)="load()">Retry</button>
        </div>
      }
      @if (loading()) { <p class="muted" role="status">Loading persisted cases...</p> }
      <div class="bulk-bar" role="group" aria-label="Bulk assignment">
        <span>{{ selectedCount() }} selected</span>
        <button class="cc-button cc-button--secondary" type="button" data-testid="bulk-claim"
          [disabled]="selectedCount() === 0 || bulkBusy()" (click)="assignSelectedToMe()">
          {{ bulkBusy() ? 'Assigning...' : 'Assign selected to me' }}</button>
        <span class="muted">Assignment is workload information only. It does not change a case.</span>
      </div>
      <div class="queue-status" role="status" aria-live="polite" aria-atomic="true" data-testid="queue-status">
        @if (notice(); as message) { <p>{{ message }}</p> }
        @if (bulkResult(); as result) {
          <p>Assigned {{ result.succeeded }} of {{ result.total }} selected {{ result.total === 1 ? 'case' : 'cases' }} to you.</p>
          @if (result.failures.length > 0) {
            <ul>@for (failure of result.failures; track failure.reference) {
              <li>{{ failure.reference }}: {{ failure.reason }}</li> }</ul>
          }
        }
      </div>
      <div class="cc-table-scroll" role="region" aria-label="Case queue, scroll horizontally on small screens" tabindex="0">
        <table class="cc-table"><caption class="cc-sr-only">Dispute cases in the authorized Agent scope</caption>
          <thead><tr>
            <th scope="col"><input type="checkbox" #selectAll data-testid="select-all" aria-label="Select all cases on this page"
              [checked]="allSelected()" [indeterminate]="someSelected()" [disabled]="items().length === 0"
              (change)="toggleAll(selectAll.checked)"></th>
            <th scope="col">Case reference</th><th scope="col">Customer</th><th scope="col">Category</th>
            <th scope="col">Amount</th><th scope="col">Status</th><th scope="col">SLA</th><th scope="col">Assigned to</th>
            <th scope="col"><span class="cc-sr-only">Actions</span></th></tr></thead>
          <tbody>@for (item of items(); track item.id; let index = $index) {
            <tr [class.is-active]="index === activeIndex()" (focusin)="activeIndex.set(index)">
              <td><input type="checkbox" #rowBox data-testid="row-select" [attr.aria-label]="'Select ' + item.reference"
                [checked]="selected().has(item.id)" (change)="toggleRow(item.id, rowBox.checked)"></td>
              <td><a class="case-link" data-testid="case-link" [routerLink]="['/agent/disputes', item.id]"><strong>{{ item.reference }}</strong></a></td>
              <td>{{ item.customerReference }}</td>
              <td><span class="tag" [class.tag-cyan]="item.category === 'DebitOrder'" [class.tag-violet]="item.category === 'BankCharge'">{{ typeLabel(item.category) }}</span></td>
              <td class="money">{{ item.amountMinorUnits | minorUnitsMoney }}</td>
              <td><cc-status-badge [label]="statusLabel(item.caseStatus)" [status]="item.caseStatus" /></td>
              <td data-testid="sla-cell">
                @if (slaLabel(item.slaStatus); as label) {
                  <cc-status-badge [label]="label" [status]="item.slaStatus" />
                  <small>Due <time [attr.datetime]="item.slaDueAt">{{ item.slaDueAt | date:'dd MMM, HH:mm':'UTC' }} UTC</time></small>
                } @else { <span class="cc-sr-only">SLA complete</span> }
              </td>
              <td data-testid="assignee-cell">
                @if (item.assignedTo; as assignee) { {{ assignee.displayName }}@if (isMine(assignee.employeeId)) { <small>You</small> } }
                @else { <span class="muted">Unassigned</span> }
              </td>
              <td class="row-actions">
                @if (item.assignedTo === undefined) {
                  <button class="cc-button cc-button--secondary" type="button" data-testid="claim"
                    [disabled]="isBusy(item.id)" [attr.aria-label]="'Claim ' + item.reference" (click)="claim(item)">Claim</button>
                } @else if (canRelease(item)) {
                  <button class="cc-button cc-button--secondary" type="button" data-testid="release"
                    [disabled]="isBusy(item.id)" [attr.aria-label]="'Release ' + item.reference" (click)="release(item)">Release</button>
                }
                <a class="row-action" [routerLink]="['/agent/disputes', item.id]" [attr.aria-label]="'Investigate ' + item.reference">Investigate &rarr;</a>
              </td>
            </tr>
          }</tbody>
        </table>
      </div>
      @if (!loading() && items().length === 0) {
        <div class="cc-empty" role="status"><h3>No matching cases</h3><p>Try another reference, customer or filter.</p></div>
      }
      <nav class="panel-footer" aria-label="Case queue pagination">
        <span>Page {{ pageNumber() }}</span>
        <div class="pager">
          <cc-page-size [value]="pageSize()" (valueChange)="setPageSize($event)" />
          <div class="paging">
            <button type="button" aria-label="Previous page" [disabled]="pageNumber() <= 1 || loading()" (click)="previousPage()">&lsaquo;</button>
            <button type="button" aria-label="Next page" data-testid="next" [disabled]="!hasNextPage() || loading()" (click)="nextPage()">&rsaquo;</button>
          </div>
        </div>
      </nav>
    </section>`,
})
export class AgentQueuePage {
  private readonly api = inject(AgentDisputesApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly searchInput = viewChild<ElementRef<HTMLInputElement>>('query');
  protected readonly typeLabel = transactionTypeLabel;
  protected readonly statusLabel = agentCaseStatusLabel;
  protected readonly slaLabel = slaStatusLabel;
  protected readonly statuses = caseStatuses;
  protected readonly categories = categories;
  protected readonly assignmentOptions = assignmentOptions;
  protected readonly presetNameLimit = maxPresetNameLength;
  protected readonly search = signal('');
  protected readonly status = signal<AgentCaseStatus | ''>('');
  protected readonly category = signal<AgentDisputeCategory | ''>('');
  protected readonly assignment = signal<AssignmentFilter>('all');
  protected readonly pageNumber = signal(1);
  protected readonly pageSize = signal<PageSize>(defaultPageSize);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly page = signal<AgentDisputePage | null>(null);
  protected readonly selected = signal<ReadonlySet<string>>(new Set());
  protected readonly activeIndex = signal(-1);
  protected readonly busyIds = signal<ReadonlySet<string>>(new Set());
  protected readonly bulkBusy = signal(false);
  protected readonly bulkResult = signal<BulkResult | null>(null);
  protected readonly notice = signal('');
  protected readonly presets = signal<QueueFilterPreset[]>(readQueueFilterPresets());
  protected readonly presetSelected = signal('');
  protected readonly presetName = signal('');
  protected readonly presetMessage = signal('');
  private cursors: (string | null)[] = [null];
  private requestId = 0;
  private activeFilters: AgentDisputeFilters = { search: '', category: '', caseStatus: '', assignment: 'all' };
  private readonly filterDebounce = createFilterDebouncer(this.destroyRef, () => this.applyFilters());
  protected readonly items = computed(() => this.page()?.items ?? []);
  protected readonly hasNextPage = computed(() => this.page()?.pageInfo.hasNextPage === true);
  protected readonly selectedCount = computed(() => this.selected().size);
  protected readonly allSelected = computed(() => this.items().length > 0 && this.selectedCount() === this.items().length);
  protected readonly someSelected = computed(() => this.selectedCount() > 0 && !this.allSelected());
  protected readonly open = computed(() => this.items().filter(item => !finishedStatuses.includes(item.caseStatus)).length);
  protected readonly needsReview = computed(() =>
    this.items().filter(item => item.caseStatus === 'Submitted' || item.caseStatus === 'UnderReview').length);
  protected readonly escalated = computed(() => this.items().filter(item => item.caseStatus === 'ManualReview').length);

  constructor() {
    this.load();
  }

  protected load(): void {
    const requestId = ++this.requestId;
    this.loading.set(true);
    this.error.set('');
    this.api.list(this.activeFilters, this.cursors[this.pageNumber() - 1] ?? null, this.pageSize())
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => {
        if (requestId === this.requestId) this.loading.set(false);
      }))
      .subscribe({
        next: value => {
          if (requestId !== this.requestId) return;
          this.page.set(value);
          this.selected.set(new Set());
          this.activeIndex.set(-1);
        },
        error: () => {
          if (requestId === this.requestId) this.error.set('Unable to load persisted cases. Check your Agent session and retry.');
        },
      });
  }

  protected applyFilters(): void {
    this.filterDebounce.cancel();
    const next: AgentDisputeFilters = {
      search: this.search().trim(), category: this.category(), caseStatus: this.status(), assignment: this.assignment(),
    };
    if (next.search === this.activeFilters.search && next.category === this.activeFilters.category
      && next.caseStatus === this.activeFilters.caseStatus && next.assignment === this.activeFilters.assignment) return;
    this.activeFilters = next;
    this.resetPaging();
  }

  protected searchTyped(value: string): void {
    this.search.set(value);
    this.filterDebounce.schedule();
  }

  protected applyNow(): void {
    this.filterDebounce.now();
  }

  protected setPageSize(size: PageSize): void {
    this.pageSize.set(size);
    this.resetPaging();
  }

  private resetPaging(): void {
    this.cursors = [null];
    this.pageNumber.set(1);
    this.load();
  }

  protected nextPage(): void {
    const cursor = this.page()?.pageInfo.nextCursor;
    if (cursor === undefined || this.loading()) return;
    this.cursors = this.cursors.slice(0, this.pageNumber());
    this.cursors.push(cursor);
    this.pageNumber.update(value => value + 1);
    this.load();
  }

  protected previousPage(): void {
    if (this.pageNumber() <= 1 || this.loading()) return;
    this.pageNumber.update(value => value - 1);
    this.load();
  }

  /** Dropdown choices apply straight away. */
  protected setStatus(value: string): void {
    this.status.set(caseStatuses.find(item => item === value) ?? '');
    this.applyFilters();
  }

  protected setCategory(value: string): void {
    this.category.set(categories.find(item => item === value) ?? '');
    this.applyFilters();
  }

  /** Assignment is a quick filter, so choosing a value searches straight away. */
  protected setAssignment(value: string): void {
    this.assignment.set(assignmentOptions.find(option => option.value === value)?.value ?? 'all');
    this.applyFilters();
  }

  // Assignment

  protected isMine(employeeId: string): boolean {
    return this.auth.user()?.employeeId === employeeId;
  }

  /** Without a known employee id the server decides; supervisors may release any assignment. */
  protected canRelease(item: AgentDisputeListItem): boolean {
    const user = this.auth.user();
    if (item.assignedTo === undefined) return false;
    return user?.employeeId === undefined || user.employeeId === null
      || user.employeeId === item.assignedTo.employeeId || user.roles.includes('Supervisor');
  }

  protected isBusy(id: string): boolean {
    return this.busyIds().has(id) || this.bulkBusy();
  }

  protected async claim(item: AgentDisputeListItem): Promise<void> {
    await this.changeAssignment(item, () => this.api.claim(item.id), `Claimed ${item.reference}.`, 'claim');
  }

  protected async release(item: AgentDisputeListItem): Promise<void> {
    await this.changeAssignment(item, () => this.api.release(item.id), `Released ${item.reference}.`, 'release');
  }

  private async changeAssignment(item: AgentDisputeListItem, request: () => ReturnType<AgentDisputesApi['claim']>,
    success: string, verb: string): Promise<void> {
    if (this.isBusy(item.id)) return;
    this.bulkResult.set(null);
    this.notice.set('');
    this.busyIds.update(ids => new Set(ids).add(item.id));
    try {
      this.patch(await firstValueFrom(request()));
      this.notice.set(success);
    } catch (error: unknown) {
      this.notice.set(`Could not ${verb} ${item.reference}: ${assignmentFailureReason(error)}.`);
    } finally {
      this.busyIds.update(ids => { const next = new Set(ids); next.delete(item.id); return next; });
    }
  }

  private patch(detail: AgentDisputeDetails): void {
    this.page.update(page => page === null ? page : {
      ...page,
      items: page.items.map(row => row.id === detail.id
        ? { ...row, assignedTo: detail.assignedTo, slaDueAt: detail.slaDueAt, slaStatus: detail.slaStatus } : row),
    });
  }

  // Bulk assignment: one claim per selected row; case state is never touched.

  protected toggleRow(id: string, checked: boolean): void {
    this.selected.update(ids => { const next = new Set(ids); if (checked) next.add(id); else next.delete(id); return next; });
  }

  protected toggleAll(checked: boolean): void {
    this.selected.set(checked ? new Set(this.items().map(item => item.id)) : new Set());
  }

  protected async assignSelectedToMe(): Promise<void> {
    const targets = this.items().filter(item => this.selected().has(item.id));
    if (targets.length === 0 || this.bulkBusy()) return;
    this.bulkBusy.set(true);
    this.notice.set('');
    this.bulkResult.set(null);
    const failures: BulkResult['failures'] = [];
    let succeeded = 0;
    for (const item of targets) {
      try {
        this.patch(await firstValueFrom(this.api.claim(item.id)));
        succeeded++;
      } catch (error: unknown) {
        failures.push({ reference: item.reference, reason: assignmentFailureReason(error) });
      }
    }
    this.bulkResult.set({ total: targets.length, succeeded, failures });
    this.selected.set(new Set());
    this.bulkBusy.set(false);
  }

  // Saved filters

  protected savePreset(): void {
    const name = this.presetName().trim();
    if (name === '') {
      this.presetMessage.set('Enter a name for the saved filter.');
      return;
    }
    const preset: QueueFilterPreset = {
      name, search: this.search().trim().slice(0, 100), category: this.category(), status: this.status(), assignment: this.assignment(),
    };
    const next = upsertPreset(this.presets(), preset);
    if (next === null) {
      this.presetMessage.set('You can keep up to 10 saved filters. Delete one first.');
      return;
    }
    this.presets.set(next);
    this.presetSelected.set(name);
    this.presetName.set('');
    this.presetMessage.set(writeQueueFilterPresets(next)
      ? `Saved filter "${name}".` : `Filter "${name}" is available for this visit only; this browser blocked saving it.`);
  }

  protected applyPreset(): void {
    const preset = this.presets().find(item => item.name === this.presetSelected());
    if (preset === undefined) return;
    this.search.set(preset.search);
    this.category.set(preset.category);
    this.status.set(preset.status);
    this.assignment.set(preset.assignment);
    this.presetMessage.set(`Applied "${preset.name}".`);
    this.applyFilters();
  }

  protected deletePreset(): void {
    const name = this.presetSelected();
    const next = this.presets().filter(item => item.name !== name);
    if (next.length === this.presets().length) return;
    this.presets.set(next);
    this.presetSelected.set('');
    writeQueueFilterPresets(next);
    this.presetMessage.set(`Deleted "${name}".`);
  }

  // Keyboard shortcuts

  @HostListener('document:keydown', ['$event'])
  protected onKeydown(event: KeyboardEvent): void {
    if (!shortcutsActive(event)) return;
    switch (event.key) {
      case '/':
        event.preventDefault();
        this.searchInput()?.nativeElement.focus();
        break;
      case 'j': this.moveActive(1); break;
      case 'k': this.moveActive(-1); break;
      case 'm':
        this.assignment.set(this.assignment() === 'mine' ? 'all' : 'mine');
        this.applyFilters();
        break;
      case 'Enter': {
        const active = this.items()[this.activeIndex()];
        if (active !== undefined && !hasNativeEnterAction(event.target)) {
          event.preventDefault();
          void this.router.navigate(['/agent/disputes', active.id]);
        }
        break;
      }
    }
  }

  private moveActive(delta: number): void {
    const count = this.items().length;
    if (count === 0) return;
    const index = Math.min(count - 1, Math.max(0, this.activeIndex() + delta));
    this.activeIndex.set(index);
    this.host.nativeElement.querySelectorAll<HTMLElement>('tbody tr a.case-link')[index]?.focus();
  }
}
