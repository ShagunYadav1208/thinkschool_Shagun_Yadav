import { Component, DestroyRef, computed, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, interval, map, of, startWith, Subject, switchMap } from 'rxjs';
import { QuotesService } from '../quotes.service';
import { Quote } from '../models/quote.model';

const HEALTH_CHECK_INTERVAL_MS = 8000;

type ApiStatus = 'checking' | 'connected' | 'disconnected';

@Component({
  selector: 'app-quote-list-detail',
  standalone: true,
  templateUrl: './quote-list-detail.html',
  styleUrl: './quote-list-detail.css',
})
export class QuoteListDetail implements OnInit {
  private readonly quotesService = inject(QuotesService);
  private readonly destroyRef = inject(DestroyRef);

  // ---- List (day-13/piece1: header bar, search, author dropdown, live API status) ----
  protected readonly quotes = signal<Quote[]>([]);
  protected readonly listLoading = signal(true);
  protected readonly searchTerm = signal('');
  protected readonly selectedAuthor = signal('');
  protected readonly apiStatus = signal<ApiStatus>('checking');

  protected readonly authors = computed(() => {
    const names = new Set(this.quotes().map((q) => q.author));
    return [...names].sort((a, b) => a.localeCompare(b));
  });

  protected readonly filteredQuotes = computed(() => {
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

  // ---- Detail (day-13/piece2: race-safe fetch on click) ----
  protected readonly selectedId = signal<number | null>(null);
  protected readonly detail = signal<Quote | null>(null);
  protected readonly detailLoading = signal(false);
  protected readonly detailError = signal(false);

  private readonly select$ = new Subject<number>();

  ngOnInit(): void {
    // List + connection status: polls on an interval rather than fetching
    // once, so `apiStatus` reflects the LATEST check and the app self-heals
    // (re-populates, indicator turns green) without a reload once the API
    // comes back. catchError lives INSIDE switchMap's own pipe - outside it,
    // the first failed check would terminate the interval permanently
    // instead of retrying 8 seconds later.
    interval(HEALTH_CHECK_INTERVAL_MS)
      .pipe(
        startWith(0),
        switchMap(() =>
          this.quotesService.getQuotes().pipe(
            map((quotes) => ({ ok: true as const, quotes })),
            catchError(() => of({ ok: false as const }))
          )
        ),
        takeUntilDestroyed(this.destroyRef)
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

    // Detail: every click pushes an id here; switchMap (not a direct
    // `.subscribe()` per click) is what fixes the stale-response race - it
    // cancels the PREVIOUS detail request the instant a new id arrives, so
    // an old, slow-to-resolve request can never overwrite a newer
    // selection. Verified with a real interleaving in verification-log.md.
    this.select$
      .pipe(
        switchMap((id) =>
          this.quotesService.getQuoteById(id).pipe(
            map((quote) => ({ ok: true as const, quote })),
            catchError(() => of({ ok: false as const }))
          )
        ),
        takeUntilDestroyed(this.destroyRef)
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

  protected onSearchInput(value: string): void {
    this.searchTerm.set(value);
  }

  protected onAuthorChange(value: string): void {
    this.selectedAuthor.set(value);
  }

  protected selectQuote(id: number): void {
    this.selectedId.set(id);
    this.detailLoading.set(true);
    this.detailError.set(false);
    this.select$.next(id);
  }
}
