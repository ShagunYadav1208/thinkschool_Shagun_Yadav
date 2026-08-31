import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { QuotesService } from '../quotes.service';
import { Quote } from '../models/quote.model';
import { AppHttpError } from '../core/http-error';

/**
 * Demonstrates the HttpClient + interceptor pipeline (auth header,
 * retry-with-backoff, ProblemDetails -> AppHttpError mapping) directly
 * against the real Week-1 QuotesApi's `GET /api/quotes?page=&size=`. Page
 * and size are free-typed on purpose - typing 0 or 500 is how a real 4xx
 * from the real API gets triggered live, not mocked.
 */
@Component({
  selector: 'app-interceptors-view',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './interceptors-view.html',
  styleUrl: './interceptors-view.css',
})
export class InterceptorsView {
  private readonly quotesService = inject(QuotesService);

  /** Exposed so the template can iterate `err.fieldErrors`' keys. */
  protected readonly Object = Object;

  protected readonly page = signal(1);
  protected readonly size = signal(10);

  protected readonly quotes = signal<Quote[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<AppHttpError | null>(null);
  protected readonly hasLoaded = signal(false);

  protected onPageInput(value: string): void {
    this.page.set(Number(value));
  }

  protected onSizeInput(value: string): void {
    this.size.set(Number(value));
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.quotesService.getQuotesPage(this.page(), this.size()).subscribe({
      next: (quotes) => {
        this.quotes.set(quotes);
        this.loading.set(false);
        this.hasLoaded.set(true);
      },
      error: (err: AppHttpError) => {
        this.quotes.set([]);
        this.error.set(err);
        this.loading.set(false);
        this.hasLoaded.set(true);
      },
    });
  }
}
