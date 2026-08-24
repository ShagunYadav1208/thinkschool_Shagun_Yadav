# Day 13 - Signals + zoneless + standalone

> **This app no longer runs independently.** Its header bar / search / author dropdown / API-status
> logic was merged into [day-13/piece2](../piece2)'s `quote-list-detail` component, so there'd be one
> app on one port with one UI instead of two overlapping ones. Everything below is the real,
> historical record of what was built and verified in *this* session - the code just isn't the thing
> running anymore. See piece2's README for the merged, currently-running version.

This exercise is different in kind from the rest of the week: the deliverable *is* the direct-and-
verify loop, not just the code at the end of it. What follows is the brief given to the agent, the
agent's resulting code verbatim, and a verification log grounded in a real, running Week-1 API and a
real headless browser - not narrated, not assumed.

Stack actually installed to do this for real: Node was upgraded from v24.13.0 to v24.19.0 (`winget
upgrade OpenJS.NodeJS.LTS`) because the current Angular CLI refuses to run below v24.15.0; Angular CLI
resolved to **22.1.5** (framework `@angular/core` `^22.1.0`) via `npx @angular/cli@latest new`. No
`chromium-cli` was available on this machine, so verification used a small Playwright driver script
instead (Playwright + Chromium installed fresh into a scratch folder, not part of the app).

## 1. The brief given to the agent

Full text in [brief-to-agent.md](brief-to-agent.md). The real API it names:

> **Target API (real, Week-1):** `QuotesApi` at `day-1/piece3/QuotesApi`
> - `GET /api/quotes/` - returns a **plain JSON array**, no envelope.
> - Quote shape, exactly: `{ "id": 1, "author": "...", "text": "..." }` - **no `createdAt` field**,
>   unlike several sibling Quotes APIs elsewhere in this repo.
>
> **Goal:** a standalone component with two signals (`quotes`, `searchTerm`), one `computed()`
> deriving the filtered list from both, rendered with `@for`/`track quote.id`; `inject()` not
> constructor injection; no `NgModule`; `provideZonelessChangeDetection()` actually enabled; three
> explicit template states (loading / empty / populated).

## 2. The agent's output, verbatim

**`src/environments/environment.ts`** (added after the CORS catch below - see that section for why):

```typescript
export const environment = {
  apiBaseUrl: '/api/quotes/',
};
```

**`src/app/models/quote.model.ts`** (final, corrected version - see "bug caught" below for the first,
wrong draft):

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

  getQuotes(): Observable<Quote[]> {
    return this.http.get<Quote[]>(environment.apiBaseUrl);
  }
}
```

**`src/app/quotes-feed/quotes-feed.ts`** (revised after the initial verification pass - a follow-up
request asked for a header bar, an author dropdown, and a live API-connection indicator, on top of
what the original brief asked for):

```typescript
import { Component, DestroyRef, computed, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, interval, map, of, startWith, switchMap } from 'rxjs';
import { QuotesService } from '../quotes.service';
import { Quote } from '../models/quote.model';

const HEALTH_CHECK_INTERVAL_MS = 8000;

type ApiStatus = 'checking' | 'connected' | 'disconnected';

@Component({
  selector: 'app-quotes-feed',
  standalone: true,
  templateUrl: './quotes-feed.html',
  styleUrl: './quotes-feed.css',
})
export class QuotesFeed implements OnInit {
  private readonly quotesService = inject(QuotesService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly quotes = signal<Quote[]>([]);
  protected readonly loading = signal(true);
  protected readonly searchTerm = signal('');
  protected readonly selectedAuthor = signal('');
  protected readonly apiStatus = signal<ApiStatus>('checking');

  // Derived from `quotes` alone - feeds the author dropdown's options.
  protected readonly authors = computed(() => {
    const names = new Set(this.quotes().map((q) => q.author));
    return [...names].sort((a, b) => a.localeCompare(b));
  });

  // The "computed value from two signals" the original brief asked for, now
  // with a third (selectedAuthor) added for the dropdown.
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

  ngOnInit(): void {
    // Polls on an interval instead of fetching once - apiStatus reflects the
    // LATEST check. catchError lives inside switchMap's inner pipe, not
    // around the whole chain: an uncaught RxJS error would terminate the
    // interval on the first failed check instead of retrying.
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
        if (result.ok) {
          this.quotes.set(result.quotes);
          this.apiStatus.set('connected');
        } else {
          this.apiStatus.set('disconnected');
        }
        this.loading.set(false);
      });
  }

  protected onSearchInput(value: string): void {
    this.searchTerm.set(value);
  }

  protected onAuthorChange(value: string): void {
    this.selectedAuthor.set(value);
  }
}
```

**`src/app/quotes-feed/quotes-feed.html`** (revised - same states as before, plus the header bar):

```html
<header class="header-bar">
  <div class="header-inner">
    <span class="brand">Quotes</span>
    <div class="controls">
      <div class="search">
        <input
          type="search"
          aria-label="Search quotes"
          placeholder="Search by author or text..."
          [value]="searchTerm()"
          (input)="onSearchInput($any($event.target).value)"
        />
      </div>

      <select
        class="author-select"
        aria-label="Filter by author"
        [value]="selectedAuthor()"
        (change)="onAuthorChange($any($event.target).value)"
      >
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

<div class="page">
  @if (loading()) {
    <div class="status"><p>Loading quotes...</p></div>
  } @else if (apiStatus() === 'disconnected' && quotes().length === 0) {
    <div class="status">
      <p class="status-title">Can't reach the API</p>
      <p class="status-hint">Retrying automatically every few seconds.</p>
    </div>
  } @else if (quotes().length === 0) {
    <div class="status">
      <p class="status-title">No quotes yet</p>
    </div>
  } @else if (filteredQuotes().length === 0) {
    <div class="status">
      <p class="status-title">No matches</p>
    </div>
  } @else {
    <ul class="quote-list">
      @for (quote of filteredQuotes(); track quote.id) {
        <li class="quote-card">
          <blockquote>{{ quote.text }}</blockquote>
          <p class="author">- {{ quote.author }}</p>
        </li>
      }
    </ul>
  }
</div>
```

**`src/app/app.config.ts`** (zoneless + HttpClient explicitly provided):

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

**`proxy.conf.json`** (generated during the CORS catch, not part of the original brief - see below):

```json
{
  "/api": { "target": "http://localhost:5310", "secure": false, "logLevel": "debug" }
}
```

`src/app/app.ts` hosts `<app-quotes-feed />`; the CLI-generated `app.spec.ts` was updated only enough
to keep compiling (`provideHttpClient()`/`provideHttpClientTesting()` added, the now-nonexistent
"Hello, quotes-feed" title assertion removed) since the brief said to leave the scaffold alone
otherwise.

## 3. Verification log

Full log with a states/edges table and screenshots in
**[verification-log.md](verification-log.md)** - summary:

- **Four states actually exercised** in a real browser against the real API: empty (zero quotes),
  populated (3 quotes via the API's own `POST`), live-filtered (typed "Grace", list narrowed 3->2
  with **no reload** - the actual zoneless/computed proof), and zero-matches-with-data-present (a
  message distinct from the empty case).
- **One concrete field-name bug caught**: the first draft assumed a `createdAt` field (a reasonable
  guess, since sibling Quotes APIs in this repo have one) - caught by reading the real
  `Quote.cs` model directly and confirming live against the running API
  (`curl -X POST .../api/quotes/` → response has no `createdAt`), then removed from both the model
  and the template.
- **One environment bug the browser test caught that a unit test never would**: calling the real API
  directly from the browser hit a genuine CORS block (`QuotesApi` has no CORS policy, and the brief
  said not to modify that project) - fixed with an Angular dev-server proxy instead of touching the
  backend.
- **What would break the contract**: a field rename fails silently (undefined in the template, no
  compile error); wrapping the response in an envelope object fails loudly (`.filter()` throws on the
  first search keystroke). Both explained with the actual mechanism in the log.

## 4. Follow-up: header bar, author dropdown, live API indicator

A later request asked for a cleaner UI plus three concrete features: a header bar, an author-filter
dropdown, and an indicator for whether the API is actually reachable. All three needed a state model
beyond the original two signals, and one needed a real correctness catch of its own:

- **`authors`**, a `computed()` over `quotes` alone, feeds the dropdown's `<option>` list -
  `[...new Set(quotes().map(q => q.author))].sort()`.
- **`selectedAuthor`**, a third signal, joins `searchTerm` inside `filteredQuotes` - both filters
  compose (verified: selecting "Grace Hopper" *and* typing "dangerous" narrows 3 quotes -> 2 -> 1).
- **`apiStatus`** (`checking`/`connected`/`disconnected`) is driven by an `interval(8000)` +
  `switchMap` poll, not a one-off check on load - the indicator reflects whether the API is reachable
  *right now*, and the app self-heals (re-populates + flips the dot green) without a manual reload
  once the API comes back.
- **The catch**: the first version of that poll put `catchError` around the *whole* RxJS chain. An
  error inside `switchMap`'s inner observable terminates the stream it's on - with `catchError`
  outside, the very first failed health check would have ended the `interval` permanently, and the
  indicator would get stuck on "disconnected" forever instead of trying again 8 seconds later. Fixed
  by moving `catchError` *inside* the `switchMap`'s own pipe, mapping failure to a `{ ok: false }`
  value instead of letting it propagate - verified by actually killing the real API mid-session,
  watching the indicator go red, restarting the API, and watching it turn green again on its own.

New screenshots (`5` through `8` in [verification-screenshots/](verification-screenshots)) cover the
dropdown, the combined author+search filter, the disconnected state (API process killed for real),
and the self-healed state after restarting it.

## GitHub link

https://github.com/thinkbridge-thinkschool/thinkschool-Shagun_Yadav/tree/main/day-13/piece1

## Notes for mentor

Two real environment gaps surfaced only because this got run in an actual browser against the actual
API, not because the code was reviewed harder: the CORS block, and the fact that Node itself needed a
minor-version bump before the current Angular CLI would even run. Neither would show up in a code
read. `quotes.db` (the real API's SQLite file) was returned to its original empty state after
verification - a test quote was `POST`ed, confirmed, then `DELETE`d for the field-name check, and
three more were added and removed for the state-verification pass.

## What did I learn this session?

Zoneless doesn't remove change detection, it makes the trigger explicit: with Zone.js, *any*
async completion (a `setTimeout`, an XHR, an event) used to schedule a check everywhere; without it,
a signal write is the only thing that schedules one, scoped to what actually reads that signal.
Watching the `<li>` count go from 3 to 2 the instant `searchTerm.set(...)` ran - with nothing else in
the app doing anything - made that concrete in a way reading the RFC never did.

## What would break this?

- The `Quote` interface is trusted, not verified - `HttpClient.get<Quote[]>()` doesn't check the
  response against the generic parameter at runtime. A server-side rename is a silent `undefined` in
  the template, not a build failure (see the log's contract-change section for the mechanism).
- `filteredQuotes()` re-filters the *entire* list on every keystroke. Fine at the ~10-100 rows this
  API returns per page; a component built the same way against a list two or three orders of
  magnitude larger would want debouncing on `searchTerm` or server-side filtering, neither of which
  this brief asked for.
- The dev proxy is a `ng serve`-only fix. A production build served as static files with the real API
  on a different origin would hit the exact same CORS wall this exercise just diagnosed - the proxy
  makes local verification possible, it doesn't make the deployed app CORS-safe.
