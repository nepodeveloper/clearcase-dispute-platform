import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, ElementRef, inject, OnInit, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, map, of, startWith, Subject, switchMap } from 'rxjs';
import { Dispute, DisputeApi, DisputeQueryError, disputeQueryError } from './dispute-api';
import { DisputeDetails } from './dispute-details';

type ViewState = { kind: 'loading' } | { kind: 'not-found' }
  | ({ kind: 'error' } & DisputeQueryError) | { kind: 'loaded'; dispute: Dispute };

@Component({
  selector: 'app-dispute-details-page',
  imports: [DisputeDetails, RouterLink],
  templateUrl: './dispute-details-page.html',
  styleUrl: './dispute-tracking.css',
})
export class DisputeDetailsPage implements OnInit {
  private readonly api = inject(DisputeApi);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly refreshes = new Subject<void>();
  private readonly heading = viewChild<ElementRef<HTMLHeadingElement>>('heading');
  protected readonly state = signal<ViewState>({ kind: 'loading' });

  ngOnInit(): void {
    this.route.paramMap.pipe(
      switchMap((params) => {
        const id = params.get('id');
        if (id === null) return of<ViewState>({ kind: 'not-found' });
        return this.refreshes.pipe(startWith(undefined), switchMap(() => this.api.get(id).pipe(
          map((dispute): ViewState => ({ kind: 'loaded', dispute })),
          catchError((error: unknown) => of<ViewState>(error instanceof HttpErrorResponse && error.status === 404
            ? { kind: 'not-found' } : { kind: 'error', ...disputeQueryError(error, 'dispute') })),
          startWith<ViewState>({ kind: 'loading' }),
        )));
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe((state) => {
      this.state.set(state);
      if (state.kind !== 'loading') this.heading()?.nativeElement.focus();
    });
  }

  protected refresh(): void { this.refreshes.next(); }
}
