import { Component, OnDestroy, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable, forkJoin, switchMap } from 'rxjs';
import { CacheService } from '../cache.service';
import { CacheMetrics } from '../models/cache-metrics.model';
import { Quote } from '../models/quote.model';
import { AppHttpError } from '../core/http-error';

interface FetchResult {
  label: string;
  ms: number;
  quote: Quote;
}

interface StampedeResult {
  concurrency: number;
  dbReads: number;
}

/**
 * Day 21: HybridCache in front of GET /api/quotes/{id} - see InfrastructureExtensions.cs and
 * QuoteEndpointExtensions.cs. In-process L1 always on; L2 is a Redis container (local Docker for
 * this session - see the README's "Redis backing" note on why not a live Azure resource this
 * time). "Fetch (cached)" vs "Fetch (uncached)" hit the same repository method, one through
 * HybridCache.GetOrCreateAsync and one bypassing it entirely, so the latency difference here is
 * real, not staged. "Run stampede test" evicts the key, resets the counters, then fires N
 * concurrent requests at once - HybridCache's single-flight behavior means only one of them
 * should ever reach the DB, which ICacheMetrics.DbReads (server-side, not a client guess) proves.
 */
@Component({
  selector: 'app-cache-view',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './cache-view.html',
  styleUrl: './cache-view.css',
})
export class CacheView implements OnDestroy {
  private readonly cacheService = inject(CacheService);
  private readonly pollHandle: ReturnType<typeof setInterval>;

  protected readonly quoteId = signal(1);
  protected readonly concurrency = signal(30);

  protected readonly metrics = signal<CacheMetrics | null>(null);
  protected readonly lastFetch = signal<FetchResult | null>(null);
  protected readonly stampedeResult = signal<StampedeResult | null>(null);

  protected readonly fetching = signal(false);
  protected readonly stampeding = signal(false);
  protected readonly error = signal<AppHttpError | null>(null);

  constructor() {
    this.refreshMetrics();
    this.pollHandle = setInterval(() => this.refreshMetrics(), 2000);
  }

  ngOnDestroy(): void {
    clearInterval(this.pollHandle);
  }

  protected onQuoteIdInput(value: string): void {
    const parsed = Number(value);
    if (Number.isFinite(parsed) && parsed > 0) this.quoteId.set(Math.trunc(parsed));
  }

  protected onConcurrencyInput(value: string): void {
    const parsed = Number(value);
    if (Number.isFinite(parsed) && parsed > 0) this.concurrency.set(Math.trunc(parsed));
  }

  protected fetchCached(): void {
    this.fetch('Cached — GET /api/quotes/{id}', this.cacheService.getCached(this.quoteId()));
  }

  protected fetchUncached(): void {
    this.fetch('Uncached — GET /api/quotes/{id}/uncached', this.cacheService.getUncached(this.quoteId()));
  }

  protected evictCurrent(): void {
    this.error.set(null);
    this.cacheService.evict(this.quoteId()).subscribe({
      next: () => this.refreshMetrics(),
      error: (err: AppHttpError) => this.error.set(err),
    });
  }

  protected resetMetrics(): void {
    this.error.set(null);
    this.stampedeResult.set(null);
    this.lastFetch.set(null);
    this.cacheService.resetMetrics().subscribe({
      next: () => this.refreshMetrics(),
      error: (err: AppHttpError) => this.error.set(err),
    });
  }

  /**
   * Evict -> reset counters -> fire `concurrency` requests for the SAME id at once -> read the
   * counters back. Every step round-trips the server, so the dbReads figure this ends with is a
   * real measurement, not something computed client-side.
   */
  protected runStampede(): void {
    const id = this.quoteId();
    const concurrency = this.concurrency();

    this.error.set(null);
    this.stampeding.set(true);
    this.stampedeResult.set(null);

    this.cacheService
      .evict(id)
      .pipe(
        switchMap(() => this.cacheService.resetMetrics()),
        switchMap(() => forkJoin(Array.from({ length: concurrency }, () => this.cacheService.getCached(id)))),
        switchMap(() => this.cacheService.getMetrics())
      )
      .subscribe({
        next: (metrics) => {
          this.metrics.set(metrics);
          this.stampedeResult.set({ concurrency, dbReads: metrics.dbReads });
          this.stampeding.set(false);
        },
        error: (err: AppHttpError) => {
          this.stampeding.set(false);
          this.error.set(err);
        },
      });
  }

  private fetch(label: string, request: Observable<Quote>): void {
    this.error.set(null);
    this.fetching.set(true);
    const start = performance.now();

    request.subscribe({
      next: (quote) => {
        const ms = performance.now() - start;
        this.fetching.set(false);
        this.lastFetch.set({ label, ms, quote });
        this.refreshMetrics();
      },
      error: (err: AppHttpError) => {
        this.fetching.set(false);
        this.error.set(err);
      },
    });
  }

  private refreshMetrics(): void {
    this.cacheService.getMetrics().subscribe({ next: (metrics) => this.metrics.set(metrics), error: () => {} });
  }
}
