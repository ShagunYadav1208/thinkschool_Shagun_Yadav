import { Injectable, computed, inject, signal } from '@angular/core';
import { catchError, map, of, Subject, switchMap } from 'rxjs';
import { QuotesService } from './quotes.service';
import { Quote } from './models/quote.model';
import { AppHttpError } from './core/http-error';

export type PageStatus = 'loading' | 'error' | 'empty' | 'loaded';

const PAGE_SIZE = 5;

/**
 * Signals-first state for a paginated, deletable quote list against the
 * real GET /api/quotes/?page&size and DELETE /api/quotes/{id} endpoints -
 * a small enough feature that a plain root-provided service + signals
 * covers it; see README for the NgRx/signal-store threshold this stays
 * under.
 */
@Injectable({ providedIn: 'root' })
export class QuoteManagementStore {
  private readonly quotesService = inject(QuotesService);
  private started = false;

  readonly page = signal(1);
  readonly quotes = signal<Quote[]>([]);
  readonly status = signal<PageStatus>('loading');
  readonly error = signal<AppHttpError | null>(null);

  readonly hasPrevious = computed(() => this.page() > 1);
  // The real API returns a plain array with no total count, so a full page
  // is the only signal that more might exist - a heuristic, not a
  // guarantee. See README "what would break this".
  readonly hasNext = computed(() => this.quotes().length === PAGE_SIZE);

  /**
   * Ids with an in-flight DELETE, so a delete button can be disabled the
   * instant it's clicked. Also closes the double-click race caught while
   * testing this store: without this guard, a second click before the
   * first request settles fires a second DELETE for the same id; the real
   * API's first DELETE returns 204, but the second - for an id that's
   * already gone - returns 404 (confirmed live via curl). The quote WAS
   * successfully deleted; a naive handler that maps every delete failure to
   * `status.set('error')` showed a scary error banner right after a delete
   * that worked.
   */
  readonly deletingIds = signal<ReadonlySet<number>>(new Set());

  private readonly fetch$ = new Subject<number>();

  start(): void {
    if (this.started) return;
    this.started = true;

    // switchMap cancels the PREVIOUS page request the instant a newer page
    // is requested, so rapid next/previous clicks can't land out of order
    // and overwrite a newer page with a slower, older response.
    this.fetch$
      .pipe(
        switchMap((page) =>
          this.quotesService.getQuotesPage(page, PAGE_SIZE).pipe(
            map((quotes) => ({ ok: true as const, quotes })),
            catchError((err: AppHttpError) => of({ ok: false as const, err }))
          )
        )
      )
      .subscribe((result) => {
        if (result.ok) {
          this.quotes.set(result.quotes);
          this.error.set(null);
          this.status.set(result.quotes.length === 0 ? 'empty' : 'loaded');
        } else {
          this.error.set(result.err);
          this.status.set('error');
        }
      });

    this.goToPage(1);
  }

  goToPage(page: number): void {
    this.page.set(page);
    this.status.set('loading');
    this.fetch$.next(page);
  }

  next(): void {
    if (this.hasNext()) this.goToPage(this.page() + 1);
  }

  previous(): void {
    if (this.hasPrevious()) this.goToPage(this.page() - 1);
  }

  deleteQuote(id: number): void {
    if (this.deletingIds().has(id)) return;
    this.deletingIds.update((ids) => new Set(ids).add(id));

    this.quotesService.deleteQuote(id).subscribe({
      next: () => this.settleDelete(id, true),
      // A 404 here means the id is already gone server-side (e.g. a second
      // click that raced the first request) - that's the delete succeeding
      // late, not a real failure, so it's treated the same as a 204.
      error: (err: AppHttpError) => this.settleDelete(id, err.status === 404, err),
    });
  }

  private settleDelete(id: number, alreadyGone: boolean, err: AppHttpError | null = null): void {
    this.deletingIds.update((ids) => {
      const next = new Set(ids);
      next.delete(id);
      return next;
    });

    if (alreadyGone) {
      this.quotes.update((current) => current.filter((q) => q.id !== id));
      if (this.quotes().length === 0) this.status.set('empty');
      return;
    }

    this.error.set(err);
    this.status.set('error');
  }
}
