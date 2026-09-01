import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';
import { AuditLogEntry, NotificationEntry, DeadLetterMessage, QuoteCreatedEventDto } from './models/service-bus.model';

@Injectable({ providedIn: 'root' })
export class ServiceBusService {
  private readonly http = inject(HttpClient);
  // `apiOrigin` is empty locally (dev-server proxy handles it) and the real
  // App Service origin in production - a relative path here would resolve
  // against the SWA's own origin on the deployed site, not the API. Caught
  // live - see environments/environment.prod.ts.
  private readonly baseUrl = `${environment.apiOrigin}/api/servicebus/`;

  /** GET /api/servicebus/audit-log - what the two competing audit-log workers have handled, newest first. */
  getAuditLog(): Observable<AuditLogEntry[]> {
    return this.http.get<AuditLogEntry[]>(`${this.baseUrl}audit-log`);
  }

  /** GET /api/servicebus/notifications - what the notifications processor has handled, newest first. */
  getNotifications(): Observable<NotificationEntry[]> {
    return this.http.get<NotificationEntry[]>(`${this.baseUrl}notifications`);
  }

  /** GET /api/servicebus/dlq - peeks the notifications subscription's dead-letter sub-queue. */
  getDeadLetterQueue(): Observable<DeadLetterMessage[]> {
    return this.http.get<DeadLetterMessage[]>(`${this.baseUrl}dlq`);
  }

  /** POST /api/servicebus/replay/{quoteId} - re-publishes the last event for this quote with the SAME message id, simulating a publisher retry. */
  replay(quoteId: number): Observable<QuoteCreatedEventDto> {
    return this.http.post<QuoteCreatedEventDto>(`${this.baseUrl}replay/${quoteId}`, {});
  }

  /** POST /api/servicebus/poison - publishes an event the notifications processor always fails on, to demonstrate dead-lettering. */
  publishPoison(): Observable<QuoteCreatedEventDto> {
    return this.http.post<QuoteCreatedEventDto>(`${this.baseUrl}poison`, {});
  }
}
