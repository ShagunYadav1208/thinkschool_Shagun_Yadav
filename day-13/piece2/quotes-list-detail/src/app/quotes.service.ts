import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';
import { Quote } from './models/quote.model';

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
}
