import { Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { QuotesService } from '../../quotes.service';
import { Quote } from '../../models/quote.model';
import { AppHttpError } from '../../core/http-error';

type DetailStatus = 'loading' | 'invalid' | 'not-found' | 'error' | 'found';

/** Matches the real API's `[Route("/{id:int}")]` constraint - a positive integer, nothing else. */
const VALID_ID_PATTERN = /^[1-9]\d*$/;

/**
 * Route param validated client-side BEFORE calling the API, because the
 * server can't tell the two cases apart: `GET /api/quotes/abc` and
 * `GET /api/quotes/999999` both come back `404` with an empty body
 * (confirmed live via curl - the `{id:int}` route constraint rejects
 * non-numeric ids before the handler that would 404 a missing one ever
 * runs). Without this check, "invalid id" and "not-found" render the exact
 * same message even though they're different problems.
 */
@Component({
  selector: 'app-quote-detail-route',
  imports: [RouterLink],
  templateUrl: './quote-detail-route.html',
  styleUrl: './quote-detail-route.css',
})
export class QuoteDetailRoute {
  private readonly quotesService = inject(QuotesService);

  /** Bound from the `:id` route segment via provideRouter's withComponentInputBinding(). */
  readonly id = input<string>();

  protected readonly status = signal<DetailStatus>('loading');
  protected readonly quote = signal<Quote | null>(null);
  protected readonly error = signal<AppHttpError | null>(null);

  constructor() {
    effect(() => {
      const rawId = this.id();

      if (!rawId || !VALID_ID_PATTERN.test(rawId)) {
        this.status.set('invalid');
        this.quote.set(null);
        return;
      }

      this.status.set('loading');
      this.quotesService.getQuoteById(Number(rawId)).subscribe({
        next: (quote) => {
          this.quote.set(quote);
          this.status.set('found');
        },
        error: (err: AppHttpError) => {
          this.error.set(err);
          this.status.set(err.status === 404 ? 'not-found' : 'error');
        },
      });
    });
  }
}
