# Day 13 - A real component from a spec

Like [day-13/piece1](../piece1), the deliverable here is the direct-and-verify loop itself: the brief
given to the agent, the agent's resulting code verbatim, and a verification log grounded in the real
Week-1 API. This piece's specific focus is the stale-response race the exercise calls out - it's
demonstrated as an actual, reproduced bug (not asserted), fixed, and re-verified with the identical
test.

Same stack as piece1: Angular CLI 22.1.5 / `@angular/core` `^22.1.0`, zoneless, a dev-server proxy to
the real `QuotesApi` (CORS reasons - see piece1's log), Playwright driving a headless Chromium since
`chromium-cli` isn't available on this machine.

## Consolidated with piece1 - one app, one port, one UI

This folder's app (`quotes-list-detail`, `http://localhost:4201`) is now the **only** running
implementation for Day 13 - piece1's header bar, live search, author dropdown, and API-connection
indicator were merged directly into this component (`quote-list-detail.ts`/`.html`/`.css`), sitting
above the list+detail layout this piece was originally built around. There is no second app on a
second port anymore: piece1's `quotes-feed` dev server was stopped, and its dev-server logic was
brought in here instead of duplicated. `filteredQuotes()` (search + author dropdown, computed over
`quotes`) is what actually populates the clickable list on the left; clicking an item is what fetches
its detail via the race-safe `switchMap` pipeline on the right - the two pieces' features compose,
they don't just sit side by side. Screenshots `9` through `13` in
[verification-screenshots/](verification-screenshots) show the merged app: loaded, author-filtered,
author+search combined, a detail loaded by click, and the stale-response race re-verified inside the
merged app specifically (not just in the pre-merge, list+detail-only version documented below).
Piece1's own folder still holds that day's original brief/output/verification-log as a historical
record of what was built and verified *that* session - the code itself no longer runs independently.

## 1. The brief given to the agent

Full text in [brief-to-agent.md](brief-to-agent.md). The real API it names:

> **Target API (real, Week-1):** `QuotesApi` at `day-1/piece3/QuotesApi`
> - `GET /api/quotes/` - list, plain array.
> - `GET /api/quotes/{id}` - detail. `200` with a quote, or `404` **with an empty body** - confirmed
>   live, not guessed.
> - Fields: `id` (number), `author` (string), `text` (string). No `createdAt`, no other fields.
>
> **Goal:** list+detail component, separate loading/error signals for list vs. detail, `inject()`,
> fully typed (no `any`), and the stale-response race actually handled and actually verified: if
> quote A is clicked then quote B before A's detail resolves, and A's response arrives *after* B's,
> the pane must end up showing B.

## 2. The agent's output, verbatim

**`src/app/models/quote.model.ts`:**

```typescript
export interface Quote {
  id: number;
  author: string;
  text: string;
}
```

**`src/app/quotes.service.ts`:**

```typescript
import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';
import { Quote } from './models/quote.model';

@Injectable({ providedIn: 'root' })
export class QuotesService {
  private readonly http = inject(HttpClient);

  /** GET /api/quotes/ - the list. */
  getQuotes(): Observable<Quote[]> {
    return this.http.get<Quote[]>(environment.apiBaseUrl);
  }

  /** GET /api/quotes/{id} - one quote's detail. 404s if id doesn't exist. */
  getQuoteById(id: number): Observable<Quote> {
    return this.http.get<Quote>(`${environment.apiBaseUrl}${id}`);
  }
}
```

**`src/app/quote-list-detail/quote-list-detail.ts`** (this is the *post-merge* version - piece1's
header bar/search/dropdown/API-polling logic now lives here too, alongside the original race-safe
detail fetch. The original, pre-merge version - list+detail only, no header - is what the
verification log's bug-reproduction section below was captured against; the merge came after, and was
re-verified against this exact code, not assumed to still work):

```typescript
import { Component, DestroyRef, computed, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, interval, map, of, startWith, Subject, switchMap } from 'rxjs';
import { QuotesService } from '../quotes.service';
import { Quote } from '../models/quote.model';

const HEALTH_CHECK_INTERVAL_MS = 8000;
type ApiStatus = 'checking' | 'connected' | 'disconnected';

@Component({
  selector: 'app-quote-list-detail',
  standalone: true,
  templateUrl: './quote-list-detail.html',
  styleUrl: './quote-list-detail.css',
})
export class QuoteListDetail implements OnInit {
  private readonly quotesService = inject(QuotesService);
  private readonly destroyRef = inject(DestroyRef);

  // List (piece1): header bar, search, author dropdown, live API status.
  protected readonly quotes = signal<Quote[]>([]);
  protected readonly listLoading = signal(true);
  protected readonly searchTerm = signal('');
  protected readonly selectedAuthor = signal('');
  protected readonly apiStatus = signal<ApiStatus>('checking');

  protected readonly authors = computed(() => {
    const names = new Set(this.quotes().map((q) => q.author));
    return [...names].sort((a, b) => a.localeCompare(b));
  });

  protected readonly filteredQuotes = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    const author = this.selectedAuthor();
    let result = this.quotes();
    if (author) result = result.filter((q) => q.author === author);
    if (term) {
      result = result.filter(
        (q) => q.author.toLowerCase().includes(term) || q.text.toLowerCase().includes(term)
      );
    }
    return result;
  });

  // Detail (piece2): race-safe fetch on click.
  protected readonly selectedId = signal<number | null>(null);
  protected readonly detail = signal<Quote | null>(null);
  protected readonly detailLoading = signal(false);
  protected readonly detailError = signal(false);
  private readonly select$ = new Subject<number>();

  ngOnInit(): void {
    interval(HEALTH_CHECK_INTERVAL_MS)
      .pipe(
        startWith(0),
        switchMap(() =>
          this.quotesService.getQuotes().pipe(
            map((quotes) => ({ ok: true as const, quotes })),
            catchError(() => of({ ok: false as const }))
          )
        ),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe((result) => {
        if (result.ok) { this.quotes.set(result.quotes); this.apiStatus.set('connected'); }
        else { this.apiStatus.set('disconnected'); }
        this.listLoading.set(false);
      });

    this.select$
      .pipe(
        switchMap((id) =>
          this.quotesService.getQuoteById(id).pipe(
            map((quote) => ({ ok: true as const, quote })),
            catchError(() => of({ ok: false as const }))
          )
        ),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe((result) => {
        this.detailLoading.set(false);
        if (result.ok) { this.detail.set(result.quote); this.detailError.set(false); }
        else { this.detail.set(null); this.detailError.set(true); }
      });
  }

  protected onSearchInput(value: string): void { this.searchTerm.set(value); }
  protected onAuthorChange(value: string): void { this.selectedAuthor.set(value); }

  protected selectQuote(id: number): void {
    this.selectedId.set(id);
    this.detailLoading.set(true);
    this.detailError.set(false);
    this.select$.next(id);
  }
}
```

**`src/app/quote-list-detail/quote-list-detail.html`** (header bar on top, list+detail below - the
list on the left now renders `filteredQuotes()`, not the raw `quotes()`, so search/dropdown actually
narrow what's clickable):

```html
<header class="header-bar">
  <div class="header-inner">
    <span class="brand">Quotes</span>
    <div class="controls">
      <input type="search" aria-label="Search quotes" placeholder="Search by author or text..."
        [value]="searchTerm()" (input)="onSearchInput($any($event.target).value)" />

      <select class="author-select" aria-label="Filter by author"
        [value]="selectedAuthor()" (change)="onAuthorChange($any($event.target).value)">
        <option value="">All authors</option>
        @for (author of authors(); track author) {
          <option [value]="author">{{ author }}</option>
        }
      </select>

      <div class="api-indicator" [class]="'api-indicator--' + apiStatus()">
        <span class="dot"></span>
        @switch (apiStatus()) {
          @case ('connected') { <span>API connected</span> }
          @case ('disconnected') { <span>API disconnected</span> }
          @default { <span>Checking API...</span> }
        }
      </div>
    </div>
  </div>
</header>

<div class="layout">
  <section class="list-pane">
    @if (listLoading()) {
      <p class="status">Loading quotes...</p>
    } @else if (apiStatus() === 'disconnected' && quotes().length === 0) {
      <p class="status status--error">Can't reach the API. Retrying automatically...</p>
    } @else if (quotes().length === 0) {
      <p class="status">No quotes yet.</p>
    } @else if (filteredQuotes().length === 0) {
      <p class="status">No matches for "{{ searchTerm() }}".</p>
    } @else {
      <ul class="quote-list">
        @for (quote of filteredQuotes(); track quote.id) {
          <li>
            <button type="button" class="quote-list-item"
              [class.quote-list-item--selected]="selectedId() === quote.id"
              (click)="selectQuote(quote.id)">
              <span class="quote-list-item-author">{{ quote.author }}</span>
              <span class="quote-list-item-preview">{{ quote.text }}</span>
            </button>
          </li>
        }
      </ul>
    }
  </section>

  <section class="detail-pane">
    @if (selectedId() === null) {
      <p class="status">Select a quote to see its detail.</p>
    } @else if (detailLoading()) {
      <p class="status">Loading detail...</p>
    } @else if (detailError()) {
      <p class="status status--error">Quote not found.</p>
    } @else if (detail(); as quote) {
      <blockquote>{{ quote.text }}</blockquote>
      <p class="author">- {{ quote.author }}</p>
    }
  </section>
</div>
```

**`src/app/app.config.ts`:**

```typescript
import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideHttpClient(),
  ]
};
```

`proxy.conf.json` (same CORS-avoidance reasoning as piece1) points `/api` at the real `QuotesApi` on
`http://localhost:5310`.

## 3. Verification log

Full log with a states/edges table and screenshots (including a real, photographed instance of the
bug, not just a claim) in **[verification-log.md](verification-log.md)** - summary:

- **6 states exercised** against the real app: initial/nothing-selected, detail loads normally, the
  stale-response race, detail 404, list error, list empty.
- **The race, reproduced as a real bug and fixed:** using `page.route()` to delay one specific quote's
  detail response by 3 seconds while another resolves instantly, clicking the slow one then
  immediately the fast one. First draft (`.subscribe()` per click, no cancellation): the fast
  response displayed correctly, then **3 seconds later the slow response silently overwrote it** -
  the list still showed the fast quote highlighted, the detail pane showed the wrong quote's text.
  Screenshotted, not just logged. Fixed by routing clicks through a `Subject` + `switchMap` instead of
  subscribing directly each time; re-ran the identical test and the late response no longer overwrites
  anything.
- **Re-verified after the piece1 merge:** author dropdown filters the list (3 -> 2), search narrows it
  further on top of that (2 -> 1), a click still loads the correct detail, and the identical
  delayed-response race test still shows the correct quote after the merge - screenshots `9`-`13` in
  [verification-screenshots/](verification-screenshots).
- **What would break the contract:** a field rename fails silently (no compile error, `undefined` in
  the template); the 404's empty body means any error handler that tries to read a message out of it
  would find nothing there.

## GitHub link

https://github.com/thinkbridge-thinkschool/thinkschool-Shagun_Yadav/tree/main/day-13/piece2

## Notes for mentor

The buggy screenshot is genuine, not staged after the fact from memory: the component was briefly
reverted to the naive `.subscribe()`-per-click version specifically to photograph the failure, then
restored to the `switchMap` fix and re-built to confirm the restore was byte-identical to the
pre-revert version and still compiled clean. `verification-screenshots/3b-race-BEFORE-fix-BUG.png`
and `.../3b-race-after-stale-response-FIXED.png` are the same click sequence, same 3-second delay,
different endings.

## What did I learn this session?

`catchError`'s placement inside vs. outside `switchMap`'s own pipe is not a style choice - it changes
whether one failed request kills every subsequent click or not. Wrapping `catchError` around the
whole `select$.pipe(...)` chain looks equivalent at a glance, but an RxJS error terminates the stream
it occurs on; put it outside `switchMap`, and the very first 404 would end the entire `select$`
subscription, and every click after that would silently do nothing - a second real bug shape hiding
right next to the one this exercise asked about.

## What would break this?

- The fix depends on every detail request going through the *same* `switchMap`. A future feature that
  added a second, independent way to trigger a detail fetch (a "refresh" button calling
  `getQuoteById` directly, say) would reintroduce exactly this race for that one path, since it
  wouldn't be routed through `select$`.
- `switchMap` cancels the previous *subscription*, not necessarily the underlying HTTP request -
  Angular's `HttpClient` does abort the actual XHR/fetch when unsubscribed, so this fix also saves the
  wasted network call, not just the wasted state update. A hand-rolled `fetch()` call not wrapped in
  an `Observable` wouldn't get that for free.
- This detail pane has no retry - a 404 or a network error is a dead end until a different quote is
  clicked. Re-clicking the *same* already-failed quote does re-issue the request (each click always
  pushes to `select$`, real or repeated id), so that specific recovery path works; there's just no way
  to retry without picking something else first and coming back.
