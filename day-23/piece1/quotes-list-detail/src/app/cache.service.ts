import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';
import { CacheMetrics } from './models/cache-metrics.model';
import { Quote } from './models/quote.model';

@Injectable({ providedIn: 'root' })
export class CacheService {
  private readonly http = inject(HttpClient);
  private readonly cacheBaseUrl = `${environment.apiOrigin}/api/cache`;
  private readonly quotesBaseUrl = `${environment.apiOrigin}/api/quotes`;

  /** GET /api/cache/metrics - process-wide counters, see ICacheMetrics.cs. */
  getMetrics(): Observable<CacheMetrics> {
    return this.http.get<CacheMetrics>(`${this.cacheBaseUrl}/metrics`);
  }

  /** POST /api/cache/metrics/reset - zeroes the counters for a fresh before/after read. */
  resetMetrics(): Observable<void> {
    return this.http.post<void>(`${this.cacheBaseUrl}/metrics/reset`, null);
  }

  /** POST /api/cache/evict/{id} - forces the next GET for this id to be a real cache miss. */
  evict(id: number): Observable<void> {
    return this.http.post<void>(`${this.cacheBaseUrl}/evict/${id}`, null);
  }

  /** GET /api/quotes/{id} - the HybridCache-fronted hot read. */
  getCached(id: number): Observable<Quote> {
    return this.http.get<Quote>(`${this.quotesBaseUrl}/${id}`);
  }

  /** GET /api/quotes/{id}/uncached - load-test/demo comparison baseline, bypasses the cache. */
  getUncached(id: number): Observable<Quote> {
    return this.http.get<Quote>(`${this.quotesBaseUrl}/${id}/uncached`);
  }
}
