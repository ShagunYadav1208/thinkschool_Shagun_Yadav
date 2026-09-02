import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';
import { OutboxRow } from './models/outbox.model';

@Injectable({ providedIn: 'root' })
export class OutboxService {
  private readonly http = inject(HttpClient);
  // `apiOrigin` (day-19) - empty locally (dev-proxy), the real App Service origin in prod. A
  // relative path here would silently break on the deployed static site - see
  // environments/environment.prod.ts.
  private readonly baseUrl = `${environment.apiOrigin}/api/outbox`;

  /** GET /api/outbox - every outbox row, newest first, regardless of ProcessedAt. */
  getOutbox(): Observable<OutboxRow[]> {
    return this.http.get<OutboxRow[]>(this.baseUrl);
  }
}
