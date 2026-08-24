import { Component, DestroyRef, computed, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, interval, map, of, startWith, switchMap } from 'rxjs';
import { QuotesService } from '../quotes.service';
import { Quote } from '../models/quote.model';

const HEALTH_CHECK_INTERVAL_MS = 8000;

type ApiStatus = 'checking' | 'connected' | 'disconnected';

@Component({
  selector: 'app-quotes-feed',
  standalone: true,
  templateUrl: './quotes-feed.html',
  styleUrl: './quotes-feed.css',
})
export class QuotesFeed implements OnInit {
  private readonly quotesService = inject(QuotesService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly quotes = signal<Quote[]>([]);
  protected readonly loading = signal(true);
  protected readonly searchTerm = signal('');
  protected readonly selectedAuthor = signal('');
  protected readonly apiStatus = signal<ApiStatus>('checking');

  // Derived from `quotes` alone - feeds the author dropdown's options.
  protected readonly authors = computed(() => {
    const names = new Set(this.quotes().map((q) => q.author));
    return [...names].sort((a, b) => a.localeCompare(b));
  });

  // The actual "computed value from two signals" this exercise asked for,
  // now with a third (selectedAuthor) added on top for the dropdown - same
  // idea, one more input.
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

  ngOnInit(): void {
    // Poll the real API on an interval, starting immediately, for as long as
    // this component is alive - `apiStatus` reflects the LATEST check, not
    // just whatever the very first load happened to see. `takeUntilDestroyed`
    // stops the interval when the component is destroyed.
    //
    // catchError lives INSIDE the switchMap's inner pipe, not around the
    // whole chain: an RxJS error terminates the stream it occurs on, and
    // without this the very first failed health check would end the
    // interval entirely - one API blip and the indicator gets stuck on
    // "disconnected" forever instead of trying again 8 seconds later.
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
        this.loading.set(false);
      });
  }

  protected onSearchInput(value: string): void {
    this.searchTerm.set(value);
  }

  protected onAuthorChange(value: string): void {
    this.selectedAuthor.set(value);
  }
}
