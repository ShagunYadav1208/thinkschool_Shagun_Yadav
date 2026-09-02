import { HttpClient, HttpParams } from '@angular/common/http';
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

  /**
   * GET /api/quotes/?page={page}&size={size} - the paginated list. `page`
   * must be >= 1 and `size` in [1, 100] or the real API returns a 400
   * ValidationProblemDetails (`errors: { page?: string[], size?: string[] }`)
   * - confirmed live via curl, not guessed. Goes through authInterceptor,
   * errorMappingInterceptor, and retryInterceptor (app.config.ts); a 4xx
   * here surfaces as a rejected AppHttpError, not a raw HttpErrorResponse.
   */
  getQuotesPage(page: number, size: number): Observable<Quote[]> {
    const params = new HttpParams().set('page', page).set('size', size);
    return this.http.get<Quote[]>(environment.apiBaseUrl, { params });
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

  /**
   * DELETE /api/quotes/{id} - 204 no content on success, or 404 (empty body)
   * if the id doesn't exist - confirmed live: created a throwaway quote,
   * deleted it (204), deleted the same id again (404). The second DELETE of
   * an id that's already gone is NOT a 204 - callers that fire two deletes
   * for the same id (e.g. a double-click) will see the second one reject.
   */
  deleteQuote(id: number): Observable<void> {
    return this.http.delete<void>(`${environment.apiBaseUrl}${id}`);
  }
}
