import { Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { QuotesService } from '../../quotes.service';
import { Quote } from '../../models/quote.model';
import { AppHttpError } from '../../core/http-error';

/**
 * The public half of the guarded pair: anyone can browse the list, but
 * `authGuard` on `quotes/:id` requires being "logged in" to open one card's
 * detail. Lazy-loaded via `quotesRoutes` - its own chunk, fetched only when
 * this route is first navigated to.
 */
@Component({
  selector: 'app-quotes-list-route',
  imports: [RouterLink],
  templateUrl: './quotes-list-route.html',
  styleUrl: './quotes-list-route.css',
})
export class QuotesListRoute implements OnInit {
  private readonly quotesService = inject(QuotesService);

  protected readonly quotes = signal<Quote[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<AppHttpError | null>(null);

  ngOnInit(): void {
    this.quotesService.getQuotesPage(1, 20).subscribe({
      next: (quotes) => {
        this.quotes.set(quotes);
        this.loading.set(false);
      },
      error: (err: AppHttpError) => {
        this.error.set(err);
        this.loading.set(false);
      },
    });
  }
}
