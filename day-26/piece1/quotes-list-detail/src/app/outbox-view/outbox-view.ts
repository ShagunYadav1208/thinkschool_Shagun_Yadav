import { Component, OnDestroy, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { OutboxService } from '../outbox.service';
import { QuotesService } from '../quotes.service';
import { ServiceBusService } from '../service-bus.service';
import { OutboxRow } from '../models/outbox.model';
import { AuditLogEntry } from '../models/service-bus.model';
import { AppHttpError } from '../core/http-error';

/**
 * Day 20: the transactional outbox. Creating a quote writes the domain row AND an outbox row in
 * one EF transaction (QuoteEndpointExtensions) - a separate relay (OutboxRelayService) is the only
 * thing that ever publishes to Service Bus, polling for unprocessed rows every 2s. "Crash-test"
 * publishes a quote whose text carries the OutboxRelayService's crash-injection marker
 * ("CRASH-RELAY:") - the relay publishes it successfully, is torn down before marking it sent, and
 * on its next tick republishes the same row (same message id) - a real duplicate delivery, safely
 * deduped by the same idempotent consumer from day 19. Watch a row's Attempts go 1 -> 2 and the
 * audit log grow two entries for the same message id, one flagged a duplicate.
 */
@Component({
  selector: 'app-outbox-view',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './outbox-view.html',
  styleUrl: './outbox-view.css',
})
export class OutboxView implements OnDestroy {
  private readonly outboxService = inject(OutboxService);
  private readonly quotesService = inject(QuotesService);
  private readonly serviceBusService = inject(ServiceBusService);
  private readonly pollHandle: ReturnType<typeof setInterval>;

  protected readonly author = signal('');
  protected readonly text = signal('');
  protected readonly publishing = signal(false);
  protected readonly error = signal<AppHttpError | null>(null);

  protected readonly outbox = signal<OutboxRow[]>([]);
  protected readonly auditLog = signal<AuditLogEntry[]>([]);

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

  protected auditEntriesFor(outboxId: string): AuditLogEntry[] {
    return this.auditLog().filter((entry) => entry.messageId === outboxId);
  }

  protected createQuote(): void {
    this.publish(this.author().trim() || 'Anonymous', this.text().trim());
  }

  protected sendCrashTest(): void {
    this.publish('Crash Test', 'CRASH-RELAY: publishes twice, handled once - see the Attempts column');
  }

  private publish(author: string, text: string): void {
    if (!text) return;

    this.publishing.set(true);
    this.error.set(null);

    this.quotesService.createQuote({ author, text }).subscribe({
      next: () => {
        this.publishing.set(false);
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

  private refresh(): void {
    // Polling ticks stay silent on failure - only an explicit publish failure surfaces a banner.
    this.outboxService.getOutbox().subscribe({ next: (rows) => this.outbox.set(rows), error: () => {} });
    this.serviceBusService.getAuditLog().subscribe({ next: (entries) => this.auditLog.set(entries), error: () => {} });
  }
}
