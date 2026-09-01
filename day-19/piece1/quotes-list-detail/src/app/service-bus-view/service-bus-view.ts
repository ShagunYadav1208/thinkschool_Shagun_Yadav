import { Component, OnDestroy, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ServiceBusService } from '../service-bus.service';
import { QuotesService } from '../quotes.service';
import { AuditLogEntry, NotificationEntry, DeadLetterMessage } from '../models/service-bus.model';
import { AppHttpError } from '../core/http-error';

/**
 * Day 19: creating a quote here publishes a QuoteCreated event to the real "quote-events" Service
 * Bus topic; two subscriptions consume it independently (audit-log with two competing workers,
 * notifications with one). "Replay" re-sends the last event with the SAME message id to prove
 * subscriber-side idempotency (not Service Bus's own duplicate detection, which is off on this
 * topic); "Send poison message" publishes an event the notifications processor always throws on,
 * to demonstrate it landing in that subscription's dead-letter queue after 3 delivery attempts.
 */
@Component({
  selector: 'app-service-bus-view',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './service-bus-view.html',
  styleUrl: './service-bus-view.css',
})
export class ServiceBusView implements OnDestroy {
  private readonly serviceBus = inject(ServiceBusService);
  private readonly quotesService = inject(QuotesService);
  private readonly pollHandle: ReturnType<typeof setInterval>;

  protected readonly author = signal('');
  protected readonly text = signal('');
  protected readonly publishing = signal(false);
  protected readonly lastQuoteId = signal<number | null>(null);
  protected readonly error = signal<AppHttpError | null>(null);

  protected readonly auditLog = signal<AuditLogEntry[]>([]);
  protected readonly notifications = signal<NotificationEntry[]>([]);
  protected readonly dlq = signal<DeadLetterMessage[]>([]);

  constructor() {
    this.refresh();
    this.pollHandle = setInterval(() => this.refresh(), 2000);
  }

  ngOnDestroy(): void {
    clearInterval(this.pollHandle);
  }

  protected onAuthorInput(value: string): void {
    this.author.set(value);
  }

  protected onTextInput(value: string): void {
    this.text.set(value);
  }

  protected formatTime(iso: string): string {
    return new Date(iso).toLocaleTimeString();
  }

  protected createQuote(): void {
    if (!this.author().trim() || !this.text().trim()) return;

    this.publishing.set(true);
    this.error.set(null);

    this.quotesService.createQuote({ author: this.author().trim(), text: this.text().trim() }).subscribe({
      next: (quote) => {
        this.publishing.set(false);
        this.lastQuoteId.set(quote.id);
        this.author.set('');
        this.text.set('');
        this.refresh();
      },
      error: (err: AppHttpError) => {
        this.publishing.set(false);
        this.error.set(err);
      },
    });
  }

  protected replayLast(): void {
    const quoteId = this.lastQuoteId();
    if (quoteId === null) return;

    this.serviceBus.replay(quoteId).subscribe({
      next: () => this.refresh(),
      error: (err: AppHttpError) => this.error.set(err),
    });
  }

  protected sendPoison(): void {
    this.serviceBus.publishPoison().subscribe({
      next: () => this.refresh(),
      error: (err: AppHttpError) => this.error.set(err),
    });
  }

  private refresh(): void {
    // Polling ticks stay silent on failure - only explicit user actions (createQuote/replay/
    // sendPoison) surface an error banner.
    this.serviceBus.getAuditLog().subscribe({ next: (entries) => this.auditLog.set(entries), error: () => {} });
    this.serviceBus.getNotifications().subscribe({ next: (entries) => this.notifications.set(entries), error: () => {} });
    this.serviceBus.getDeadLetterQueue().subscribe({ next: (entries) => this.dlq.set(entries), error: () => {} });
  }
}
