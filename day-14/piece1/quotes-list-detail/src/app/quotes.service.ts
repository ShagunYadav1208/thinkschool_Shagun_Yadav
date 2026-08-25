import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';
import { Quote } from './models/quote.model';

export interface CreateQuoteRequest {
  author: string;
  text: string;
}

@Injectable({ providedIn: 'root' })
export class QuotesService {
  private readonly http = inject(HttpClient);

  /** GET /api/quotes/ - the list. */
  getQuotes(): Observable<Quote[]> {
    return this.http.get<Quote[]>(environment.apiBaseUrl);
  }

  /** GET /api/quotes/{id} - one quote's detail. 404s if id doesn't exist. */
  getQuoteById(id: number): Observable<Quote> {
    return this.http.get<Quote>(`${environment.apiBaseUrl}${id}`);
  }

  /**
   * POST /api/quotes/ - create a quote. 201 with the created Quote, or 400
   * with `{ errors: { author?: string[], text?: string[] } }` on validation
   * failure - both confirmed live against the running API, not guessed.
   */
  createQuote(request: CreateQuoteRequest): Observable<Quote> {
    return this.http.post<Quote>(environment.apiBaseUrl, request);
  }
}
