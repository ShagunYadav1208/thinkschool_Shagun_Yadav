import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { JobRecord } from './models/job.model';

@Injectable({ providedIn: 'root' })
export class JobsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/jobs/';

  /** POST /api/jobs/quote-analysis/{quoteId} - 202 Accepted with the queued JobRecord. */
  enqueueQuoteAnalysis(quoteId: number): Observable<JobRecord> {
    return this.http.post<JobRecord>(`${this.baseUrl}quote-analysis/${quoteId}`, {});
  }

  /** GET /api/jobs/ - every job this process has seen, newest first. In-memory only - restarting the API clears it. */
  getJobs(): Observable<JobRecord[]> {
    return this.http.get<JobRecord[]>(this.baseUrl);
  }
}
