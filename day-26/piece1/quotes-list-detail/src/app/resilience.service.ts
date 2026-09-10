import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';
import { EnrichResult, FlakyDependencyMode, FlakyDependencySnapshot, ResilienceMetrics } from './models/resilience.model';

@Injectable({ providedIn: 'root' })
export class ResilienceService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiOrigin}/api/resilience`;

  /** GET /api/resilience/metrics - process-wide counters + the circuit's transition timeline. */
  getMetrics(): Observable<ResilienceMetrics> {
    return this.http.get<ResilienceMetrics>(`${this.baseUrl}/metrics`);
  }

  /** POST /api/resilience/metrics/reset - zeroes counters and clears the timeline. */
  resetMetrics(): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/metrics/reset`, null);
  }

  /** GET /api/resilience/dependency - the flaky dependency's current configured behavior. */
  getDependency(): Observable<FlakyDependencySnapshot> {
    return this.http.get<FlakyDependencySnapshot>(`${this.baseUrl}/dependency`);
  }

  /** POST /api/resilience/dependency/configure - controls FlakyDependencyState. */
  configureDependency(mode: FlakyDependencyMode, latencyMs?: number, failureRatePercent?: number): Observable<FlakyDependencySnapshot> {
    return this.http.post<FlakyDependencySnapshot>(`${this.baseUrl}/dependency/configure`, {
      mode,
      latencyMs,
      failureRatePercent,
    });
  }

  /** GET /api/resilience/enrich/{id} - the actual call through the Polly pipeline. Always
   * resolves 200 with a classified outcome, even on failure - see ResilienceEndpointExtensions.cs. */
  enrich(quoteId: number): Observable<EnrichResult> {
    return this.http.get<EnrichResult>(`${this.baseUrl}/enrich/${quoteId}`);
  }
}
