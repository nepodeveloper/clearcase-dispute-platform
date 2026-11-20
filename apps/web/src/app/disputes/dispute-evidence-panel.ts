import { DatePipe } from '@angular/common';
import { HttpErrorResponse, HttpEventType } from '@angular/common/http';
import { Component, DestroyRef, effect, ElementRef, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { CustomerEvidence, DisputeApi, problemCode } from './dispute-api';

/** Mirrors the server limits for early feedback only; the server remains the authority (magic bytes, scan, count). */
export const evidenceLimits = {
  maxBytes: 5 * 1024 * 1024,
  maxFiles: 5,
  contentTypes: ['application/pdf', 'image/png', 'image/jpeg'],
  extensions: ['pdf', 'png', 'jpg', 'jpeg'],
  noteMaxLength: 200,
} as const;

/** Returns a user-facing problem with the chosen file, or null when it looks acceptable. */
export function evidenceFileProblem(file: File, existingCount: number): string | null {
  if (existingCount >= evidenceLimits.maxFiles)
    return `This dispute already has the maximum of ${evidenceLimits.maxFiles} files.`;
  if (file.size === 0) return 'That file is empty. Choose a different file.';
  if (file.size > evidenceLimits.maxBytes) return 'That file is larger than 5 MB. Choose a smaller file.';
  const extension = file.name.split('.').pop()?.toLowerCase() ?? '';
  const typeKnown = file.type === '' ? evidenceLimits.extensions.some(allowed => allowed === extension)
    : evidenceLimits.contentTypes.some(allowed => allowed === file.type);
  return typeKnown ? null : 'Only PDF, PNG or JPEG files can be uploaded.';
}

export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  return bytes < 1024 * 1024 ? `${(bytes / 1024).toFixed(1)} KB` : `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

type ListState = { kind: 'loading' } | { kind: 'error' } | { kind: 'loaded' };

@Component({
  selector: 'cc-dispute-evidence',
  imports: [DatePipe, ReactiveFormsModule],
  template: `
    <section class="panel" aria-labelledby="evidence-heading">
      <h2 id="evidence-heading">Supporting documents</h2>
      <p class="muted">PDF, PNG or JPEG, up to 5 MB each and up to {{ limits.maxFiles }} files per dispute.
        Files are security-scanned before they are stored and are visible to the reviewers handling your case.</p>
      @switch (list().kind) {
        @case ('loading') { <p class="muted" role="status">Loading documents...</p> }
        @case ('error') {
          <div class="state-message" role="alert"><p>Unable to load your documents.</p>
            <button class="cc-button cc-button--secondary" type="button" data-testid="evidence-retry" (click)="load()">Retry</button></div>
        }
        @default {
          @if (items().length === 0) {
            <p class="muted" data-testid="evidence-empty">No documents uploaded yet.</p>
          } @else {
            <ul class="evidence-list" aria-label="Uploaded documents">
              @for (item of items(); track item.id) {
                <li>
                  <div>
                    <strong>{{ item.fileName }}</strong>
                    <small>{{ sizeLabel(item.sizeBytes) }} &middot; uploaded
                      <time [attr.datetime]="item.uploadedAt">{{ item.uploadedAt | date:'mediumDate' }}</time></small>
                    @if (item.note) { <small>Note: {{ item.note }}</small> }
                  </div>
                  <a class="cc-button cc-button--secondary" data-testid="evidence-download" [href]="contentUrl(item)"
                    [attr.download]="item.fileName" [attr.aria-label]="'Download ' + item.fileName">Download</a>
                </li>
              }
            </ul>
          }
        }
      }
      @if (closed()) {
        <p class="notice" data-testid="evidence-closed">This dispute is closed, so new documents cannot be added.</p>
      } @else if (items().length >= limits.maxFiles) {
        <p class="notice" data-testid="evidence-limit">You have reached the limit of {{ limits.maxFiles }} documents for this dispute.</p>
      } @else {
        <form class="form-stack evidence-form" (submit)="upload($event)" novalidate [attr.aria-busy]="uploading()">
          <div class="cc-field"><label for="evidence-file">Choose a document</label>
            <input #fileInput id="evidence-file" class="cc-control" type="file" [disabled]="uploading()"
              accept=".pdf,.png,.jpg,.jpeg,application/pdf,image/png,image/jpeg" (change)="fileChosen($event)"
              aria-describedby="evidence-file-hint evidence-error" [attr.aria-invalid]="error() !== ''">
            <span class="field-hint" id="evidence-file-hint">PDF, PNG or JPEG. Maximum 5 MB.</span>
          </div>
          <div class="cc-field"><label for="evidence-note">Note (optional)</label>
            <input id="evidence-note" class="cc-control" type="text" [formControl]="note" [attr.maxlength]="limits.noteMaxLength"
              autocomplete="off" aria-describedby="evidence-note-hint">
            <span class="field-hint" id="evidence-note-hint">Up to {{ limits.noteMaxLength }} characters, for example what the document shows.</span>
          </div>
          <div id="evidence-error">@if (error()) { <p class="cc-form-error" role="alert">{{ error() }}</p> }</div>
          @if (uploading()) {
            <div data-testid="evidence-progress">
              <progress max="100" [value]="progress()" aria-label="Upload progress"></progress>
              <span role="status">Uploading and scanning... {{ progress() }}%</span>
            </div>
          }
          <button class="cc-button cc-button--primary" type="submit" [disabled]="uploading()">
            {{ uploading() ? 'Uploading...' : 'Upload document' }}</button>
        </form>
      }
      <p class="muted" role="status" data-testid="evidence-status">{{ confirmation() }}</p>
    </section>`,
})
export class DisputeEvidencePanel {
  readonly disputeId = input.required<string>();
  readonly closed = input(false);
  /** Emitted when the server says the dispute is closed so the page can refresh its status. */
  readonly refreshRequested = output<void>();
  private readonly api = inject(DisputeApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');
  protected readonly limits = evidenceLimits;
  protected readonly list = signal<ListState>({ kind: 'loading' });
  protected readonly items = signal<CustomerEvidence[]>([]);
  protected readonly error = signal('');
  protected readonly confirmation = signal('');
  protected readonly uploading = signal(false);
  protected readonly progress = signal(0);
  protected readonly note = new FormControl('', { nonNullable: true, validators: Validators.maxLength(evidenceLimits.noteMaxLength) });
  private file: File | null = null;

  constructor() {
    effect(() => {
      this.disputeId();
      untracked(() => this.load());
    });
  }

  protected load(): void {
    this.list.set({ kind: 'loading' });
    this.api.listEvidence(this.disputeId()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => { this.items.set(result.items); this.list.set({ kind: 'loaded' }); },
      error: () => this.list.set({ kind: 'error' }),
    });
  }

  protected contentUrl(item: CustomerEvidence): string {
    return this.api.evidenceContentUrl(this.disputeId(), item.id);
  }

  protected readonly sizeLabel = formatFileSize;

  protected fileChosen(event: Event): void {
    const input = event.target instanceof HTMLInputElement ? event.target : null;
    this.file = input?.files?.item(0) ?? null;
    this.error.set('');
    this.confirmation.set('');
    if (this.file !== null) this.error.set(evidenceFileProblem(this.file, this.items().length) ?? '');
  }

  protected upload(event: Event): void {
    event.preventDefault();
    if (this.uploading()) return;
    const file = this.file;
    if (file === null) {
      this.error.set('Choose a document to upload.');
      return;
    }
    const problem = evidenceFileProblem(file, this.items().length);
    if (problem !== null || this.note.invalid) {
      this.error.set(problem ?? `Keep the note to ${evidenceLimits.noteMaxLength} characters or fewer.`);
      return;
    }
    this.error.set('');
    this.confirmation.set('');
    this.progress.set(0);
    this.uploading.set(true);
    this.api.uploadEvidence(this.disputeId(), file, this.note.value.trim()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => {
        if (result.type === HttpEventType.UploadProgress && result.total !== undefined && result.total > 0) {
          this.progress.set(Math.min(100, Math.round(100 * result.loaded / result.total)));
        } else if (result.type === HttpEventType.Response && result.body !== null) {
          const uploaded = result.body;
          this.items.update(items => [...items, uploaded]);
          this.confirmation.set(`${uploaded.fileName} was uploaded after a security scan.`);
          this.resetForm();
          this.uploading.set(false);
        }
      },
      error: (failure: unknown) => {
        this.uploading.set(false);
        this.error.set(this.failureMessage(failure));
        if (problemCode(failure) === 'dispute.closed') this.refreshRequested.emit();
        if (problemCode(failure) === 'evidence.limitReached') this.load();
      },
    });
  }

  private resetForm(): void {
    this.file = null;
    this.note.reset();
    const input = this.fileInput()?.nativeElement;
    if (input !== undefined) input.value = '';
  }

  private failureMessage(failure: unknown): string {
    switch (problemCode(failure)) {
      case 'evidence.invalidFile': return 'That file could not be accepted. Use a non-empty PDF, PNG or JPEG of 5 MB or less.';
      case 'evidence.limitReached': return `This dispute already has the maximum of ${evidenceLimits.maxFiles} files.`;
      case 'evidence.rejectedByScan': return 'The file was rejected by our security scan and was not stored. Choose a different file.';
      case 'evidence.scanUnavailable': return 'Security scanning is unavailable right now, so nothing was stored. Please try again later.';
      case 'dispute.closed': return 'This dispute is closed, so new documents cannot be added.';
      default:
        return failure instanceof HttpErrorResponse && failure.status === 413
          ? 'That file is too large. Choose a file of 5 MB or less.'
          : 'The upload failed and nothing was stored. Check your connection and try again.';
    }
  }
}
