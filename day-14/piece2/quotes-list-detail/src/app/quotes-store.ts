import { Injectable, computed, inject, signal } from '@angular/core';
import { catchError, interval, map, of, startWith, Subject, switchMap } from 'rxjs';
import { QuotesService } from './quotes.service';
import { Quote } from './models/quote.model';

const HEALTH_CHECK_INTERVAL_MS = 8000;

export type ApiStatus = 'checking' | 'connected' | 'disconnected';

/**
 * Single shared source of truth for quote data across all three tabs
 * (Explore / Create / All Quotes) - moved here from the old
 * QuoteListDetail component so a quote created on the Create tab shows up
 * in Explore/All Quotes immediately without any tab needing to be mounted
 * at the same time.
 */
@Injectable({ providedIn: 'root' })
export class QuotesStore {
  private readonly quotesService = inject(QuotesService);
  private started = false;

  readonly quotes = signal<Quote[]>([]);
  readonly listLoading = signal(true);
  readonly searchTerm = signal('');
  readonly selectedAuthor = signal('');
  readonly apiStatus = signal<ApiStatus>('checking');

  readonly authors = computed(() => {
    const names = new Set(this.quotes().map((q) => q.author));
    return [...names].sort((a, b) => a.localeCompare(b));
  });

  readonly filteredQuotes = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    const author = this.selectedAuthor();
    let result = this.quotes();
    if (author) {
      result = result.filter((q) => q.author === author);
    }
    if (term) {
      result = result.filter(
        (q) => q.author.toLowerCase().includes(term) || q.text.toLowerCase().includes(term)
      );
    }
    return result;
  });

  readonly selectedId = signal<number | null>(null);
  readonly detail = signal<Quote | null>(null);
  readonly detailLoading = signal(false);
  readonly detailError = signal(false);

  private readonly select$ = new Subject<number>();

  /**
   * Wires up the list-polling and detail-fetch pipelines. Guarded so it's
   * safe to call from every tab's constructor - only the first call does
   * anything, since this store is a root singleton that outlives any one
   * tab's component.
   */
  start(): void {
    if (this.started) return;
    this.started = true;

    // Polls on an interval rather than fetching once, so `apiStatus`
    // reflects the LATEST check and the app self-heals once the API comes
    // back. catchError lives INSIDE switchMap's own pipe - outside it, the
    // first failed check would terminate the interval permanently instead
    // of retrying 8 seconds later.
    interval(HEALTH_CHECK_INTERVAL_MS)
      .pipe(
        startWith(0),
        switchMap(() =>
          this.quotesService.getQuotes().pipe(
            map((quotes) => ({ ok: true as const, quotes })),
            catchError(() => of({ ok: false as const }))
          )
        )
      )
      .subscribe((result) => {
        if (result.ok) {
          this.quotes.set(result.quotes);
          this.apiStatus.set('connected');
        } else {
          this.apiStatus.set('disconnected');
        }
        this.listLoading.set(false);
      });

    // switchMap (not a direct `.subscribe()` per click) cancels the
    // PREVIOUS detail request the instant a new id arrives, so a slow old
    // request can never overwrite a newer selection.
    this.select$
      .pipe(
        switchMap((id) =>
          this.quotesService.getQuoteById(id).pipe(
            map((quote) => ({ ok: true as const, quote })),
            catchError(() => of({ ok: false as const }))
          )
        )
      )
      .subscribe((result) => {
        this.detailLoading.set(false);
        if (result.ok) {
          this.detail.set(result.quote);
          this.detailError.set(false);
        } else {
          this.detail.set(null);
          this.detailError.set(true);
        }
      });
  }

  onSearchInput(value: string): void {
    this.searchTerm.set(value);
  }

  onAuthorChange(value: string): void {
    this.selectedAuthor.set(value);
  }

  selectQuote(id: number): void {
    this.selectedId.set(id);
    this.detailLoading.set(true);
    this.detailError.set(false);
    this.select$.next(id);
  }

  /** The create-quote form emits the server's real response (id included) - prepended straight into the shared list, so every tab sees it immediately, no reload and no extra GET. */
  onQuoteCreated(quote: Quote): void {
    this.quotes.update((current) => [quote, ...current]);
  }
}
