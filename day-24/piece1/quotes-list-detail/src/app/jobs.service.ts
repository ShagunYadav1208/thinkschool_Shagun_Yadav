import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';
import { JobRecord } from './models/job.model';

@Injectable({ providedIn: 'root' })
export class JobsService {
  private readonly http = inject(HttpClient);
  // `apiOrigin` is empty locally (dev-server proxy handles it) and the real
  // App Service origin in production - a relative path here would resolve
  // against the SWA's own origin on the deployed site, not the API. Caught
  // live - see environments/environment.prod.ts.
  private readonly baseUrl = `${environment.apiOrigin}/api/jobs/`;

  /** POST /api/jobs/quote-analysis/{quoteId} - 202 Accepted with the queued JobRecord. */
  enqueueQuoteAnalysis(quoteId: number): Observable<JobRecord> {
    return this.http.post<JobRecord>(`${this.baseUrl}quote-analysis/${quoteId}`, {});
  }

  /** GET /api/jobs/ - every job this process has seen, newest first. In-memory only - restarting the API clears it. */
  getJobs(): Observable<JobRecord[]> {
    return this.http.get<JobRecord[]>(this.baseUrl);
  }
}
