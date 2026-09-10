import { Component, OnDestroy, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { JobsService } from '../jobs.service';
import { JobRecord, JOB_STATUS_LABEL } from '../models/job.model';
import { AppHttpError } from '../core/http-error';

/**
 * Day 18: background jobs. Enqueues a ~5s "quote analysis" job onto QuotesApi's
 * Channel-backed queue (202 Accepted, returns immediately) and polls GET /api/jobs every second
 * to show it move Queued -> Running -> Completed. There's no push channel here (no
 * SignalR/WebSocket) - polling is the simplest way to observe an in-process-only job store from
 * the browser, and matches the exercise's own tradeoff of an in-memory queue over a persisted one.
 */
@Component({
  selector: 'app-background-jobs-view',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './background-jobs-view.html',
  styleUrl: './background-jobs-view.css',
})
export class BackgroundJobsView implements OnDestroy {
  private readonly jobsService = inject(JobsService);
  private readonly pollHandle: ReturnType<typeof setInterval>;

  protected readonly statusLabel = JOB_STATUS_LABEL;
  protected readonly quoteId = signal(1);
  protected readonly jobs = signal<JobRecord[]>([]);
  protected readonly enqueueing = signal(false);
  protected readonly error = signal<AppHttpError | null>(null);

  constructor() {
    this.refresh();
    this.pollHandle = setInterval(() => this.refresh(), 1000);
  }

  ngOnDestroy(): void {
    clearInterval(this.pollHandle);
  }

  protected onQuoteIdInput(value: string): void {
    this.quoteId.set(Number(value));
  }

  protected statusClass(job: JobRecord): string {
    return `badge--${this.statusLabel[job.status].toLowerCase()}`;
  }

  protected formatTime(iso: string): string {
    return new Date(iso).toLocaleTimeString();
  }

  protected enqueue(): void {
    this.enqueueing.set(true);
    this.error.set(null);

    this.jobsService.enqueueQuoteAnalysis(this.quoteId()).subscribe({
      next: () => {
        this.enqueueing.set(false);
        this.refresh();
      },
      error: (err: AppHttpError) => {
        this.enqueueing.set(false);
        this.error.set(err);
      },
    });
  }

  private refresh(): void {
    // A transient failure on a background polling tick shouldn't surface as a page-level error -
    // only an explicit enqueue() failure does that.
    this.jobsService.getJobs().subscribe({
      next: (jobs) => this.jobs.set(jobs),
      error: () => {},
    });
  }
}
