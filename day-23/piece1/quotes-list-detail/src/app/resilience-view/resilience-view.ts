import { Component, OnDestroy, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ResilienceService } from '../resilience.service';
import { EnrichResult, FlakyDependencyMode, FlakyDependencySnapshot, ResilienceMetrics } from '../models/resilience.model';
import { AppHttpError } from '../core/http-error';

interface LogEntry {
  index: number;
  outcome: EnrichResult['outcome'];
  detail: string | null;
}

const CIRCUIT_STATE_NAMES = ['Closed', 'Open', 'HalfOpen', 'Isolated'];
const MODES: FlakyDependencyMode[] = ['Healthy', 'AlwaysFail', 'Slow', 'Intermittent'];

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/**
 * Day 22: Polly wraps the "quote enrichment" outbound dependency (GET /api/resilience/enrich/{id})
 * - see InfrastructureExtensions.cs for the pipeline (bulkhead -> retry -> circuit breaker ->
 * per-attempt timeout, in that order) and FlakyDependencyHandler for what's actually behind the
 * HttpClient: a deterministic, controllable stand-in for a real third-party API, so the breaker's
 * open -> half-open -> closed cycle can be reproduced on demand from this tab instead of waiting on
 * a real service's actual uptime.
 */
@Component({
  selector: 'app-resilience-view',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './resilience-view.html',
  styleUrl: './resilience-view.css',
})
export class ResilienceView implements OnDestroy {
  private readonly resilienceService = inject(ResilienceService);
  private readonly pollHandle: ReturnType<typeof setInterval>;

  protected readonly modes = MODES;
  protected readonly quoteId = signal(1);
  protected readonly callCount = signal(6);

  protected readonly selectedMode = signal<FlakyDependencyMode>('Healthy');
  protected readonly latencyMs = signal(30);
  protected readonly failureRatePercent = signal(100);

  protected readonly metrics = signal<ResilienceMetrics | null>(null);
  protected readonly dependency = signal<FlakyDependencySnapshot | null>(null);
  protected readonly log = signal<LogEntry[]>([]);

  protected readonly firing = signal(false);
  protected readonly configuring = signal(false);
  protected readonly runningDemo = signal(false);
  protected readonly demoNarration = signal<string[]>([]);
  protected readonly error = signal<AppHttpError | null>(null);

  constructor() {
    this.refresh();
    this.pollHandle = setInterval(() => this.refresh(), 2000);
  }

  ngOnDestroy(): void {
    clearInterval(this.pollHandle);
  }

  protected circuitStateName(state: number): string {
    return CIRCUIT_STATE_NAMES[state] ?? String(state);
  }

  protected formatTime(iso: string): string {
    return new Date(iso).toLocaleTimeString();
  }

  protected onQuoteIdInput(value: string): void {
    const parsed = Number(value);
    if (Number.isFinite(parsed) && parsed > 0) this.quoteId.set(Math.trunc(parsed));
  }

  protected onCallCountInput(value: string): void {
    const parsed = Number(value);
    if (Number.isFinite(parsed) && parsed > 0) this.callCount.set(Math.trunc(parsed));
  }

  protected onLatencyInput(value: string): void {
    const parsed = Number(value);
    if (Number.isFinite(parsed) && parsed >= 0) this.latencyMs.set(Math.trunc(parsed));
  }

  protected onFailureRateInput(value: string): void {
    const parsed = Number(value);
    if (Number.isFinite(parsed) && parsed >= 0 && parsed <= 100) this.failureRatePercent.set(Math.trunc(parsed));
  }

  protected applyDependencyConfig(): void {
    this.error.set(null);
    this.configuring.set(true);

    this.resilienceService.configureDependency(this.selectedMode(), this.latencyMs(), this.failureRatePercent()).subscribe({
      next: (snapshot) => {
        this.dependency.set(snapshot);
        this.configuring.set(false);
      },
      error: (err: AppHttpError) => {
        this.configuring.set(false);
        this.error.set(err);
      },
    });
  }

  protected resetMetrics(): void {
    this.error.set(null);
    this.log.set([]);
    this.demoNarration.set([]);
    this.resilienceService.resetMetrics().subscribe({
      next: () => this.refreshMetrics(),
      error: (err: AppHttpError) => this.error.set(err),
    });
  }

  /** Fires `callCount` enrich calls one after another (not all at once) - sequential on purpose,
   * so the live counters/circuit-state panel visibly steps through each call's effect instead of
   * a burst that resolves before anyone can watch it. */
  protected async fireBatch(): Promise<void> {
    this.error.set(null);
    this.firing.set(true);
    this.log.set([]);

    const id = this.quoteId();
    const count = this.callCount();

    try {
      for (let i = 1; i <= count; i++) {
        const result = await firstValueFrom(this.resilienceService.enrich(id));
        this.log.update((entries) => [...entries, { index: i, outcome: result.outcome, detail: result.detail }]);
        this.refreshMetrics();
      }
    } catch (err) {
      this.error.set(err as AppHttpError);
    } finally {
      this.firing.set(false);
    }
  }

  /**
   * The full proof this exercise asks for, automated: make the dependency fail sustained enough
   * to trip the breaker, confirm it's actually Open (further calls fast-rejected, not retried),
   * wait out BreakDuration, heal the dependency, and confirm the half-open trial closes it again.
   * Every step re-reads server-side state (never assumes timing) - see the narration log this
   * produces for the real sequence observed.
   */
  protected async runBreakerDemo(): Promise<void> {
    this.error.set(null);
    this.runningDemo.set(true);
    this.log.set([]);
    this.demoNarration.set([]);
    const narrate = (line: string) => this.demoNarration.update((lines) => [...lines, line]);

    const id = this.quoteId();

    try {
      narrate('Configuring dependency to AlwaysFail...');
      await firstValueFrom(this.resilienceService.configureDependency('AlwaysFail', 30));
      await firstValueFrom(this.resilienceService.resetMetrics());
      this.refreshDependency();

      narrate('Firing calls until the circuit opens (max 10)...');
      let state = await this.currentCircuitState();
      let calls = 0;
      while (state !== 'Open' && calls < 10) {
        calls++;
        const result = await firstValueFrom(this.resilienceService.enrich(id));
        this.log.update((entries) => [...entries, { index: calls, outcome: result.outcome, detail: result.detail }]);
        state = await this.currentCircuitState();
      }

      if (state !== 'Open') {
        narrate(`Circuit did not open after ${calls} calls - stopping.`);
        return;
      }
      narrate(`Circuit OPEN after ${calls} call(s). Confirming further calls fail fast...`);

      const before = (await firstValueFrom(this.resilienceService.getMetrics())).dependencyAttempts;
      const rejected = await firstValueFrom(this.resilienceService.enrich(id));
      this.log.update((entries) => [...entries, { index: calls + 1, outcome: rejected.outcome, detail: rejected.detail }]);
      const after = (await firstValueFrom(this.resilienceService.getMetrics())).dependencyAttempts;
      narrate(
        after === before
          ? 'Confirmed: that call never reached the dependency (dependencyAttempts unchanged) - a real fail-fast rejection.'
          : 'Unexpected: dependencyAttempts increased while the circuit should be open.'
      );

      narrate('Waiting 9s for BreakDuration (8s) to elapse...');
      await sleep(9000);

      narrate('Configuring dependency back to Healthy...');
      await firstValueFrom(this.resilienceService.configureDependency('Healthy', 30));
      this.refreshDependency();

      narrate('Firing the half-open trial call...');
      const trial = await firstValueFrom(this.resilienceService.enrich(id));
      this.log.update((entries) => [...entries, { index: calls + 2, outcome: trial.outcome, detail: trial.detail }]);

      const finalState = await this.currentCircuitState();
      narrate(`Circuit is now ${finalState} after the trial call.`);
    } catch (err) {
      this.error.set(err as AppHttpError);
    } finally {
      this.runningDemo.set(false);
      this.refreshMetrics();
    }
  }

  private async currentCircuitState(): Promise<string> {
    const snapshot = await firstValueFrom(this.resilienceService.getMetrics());
    this.metrics.set(snapshot);
    return snapshot.circuitState;
  }

  private refresh(): void {
    this.refreshMetrics();
    this.refreshDependency();
  }

  private refreshMetrics(): void {
    this.resilienceService.getMetrics().subscribe({ next: (metrics) => this.metrics.set(metrics), error: () => {} });
  }

  private refreshDependency(): void {
    this.resilienceService.getDependency().subscribe({
      next: (snapshot) => {
        this.dependency.set(snapshot);
        this.selectedMode.set(snapshot.mode);
      },
      error: () => {},
    });
  }
}
