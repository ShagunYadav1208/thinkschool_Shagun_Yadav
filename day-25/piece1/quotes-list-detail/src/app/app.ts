import { Component, effect, inject, signal } from '@angular/core';
import { AuthService } from './core/auth.service';
import { CreateQuoteForm } from './create-quote-form/create-quote-form';
import { CreateQuoteFormSignal } from './create-quote-form-signal/create-quote-form-signal';
import { ExploreView } from './explore-view/explore-view';
import { AllQuotesView } from './all-quotes-view/all-quotes-view';
import { InterceptorsView } from './interceptors-view/interceptors-view';
import { RoutingView } from './routing-view/routing-view';
import { QuoteManagementView } from './quote-management-view/quote-management-view';
import { BackgroundJobsView } from './background-jobs-view/background-jobs-view';
import { ServiceBusView } from './service-bus-view/service-bus-view';
import { OutboxView } from './outbox-view/outbox-view';
import { CacheView } from './cache-view/cache-view';
import { ResilienceView } from './resilience-view/resilience-view';
import { QuotesStore } from './quotes-store';
import { Quote } from './models/quote.model';

type Tab =
  | 'explore'
  | 'create'
  | 'signal-forms'
  | 'all'
  | 'interceptors'
  | 'routing'
  | 'manage'
  | 'jobs'
  | 'service-bus'
  | 'outbox'
  | 'cache'
  | 'resilience';

@Component({
  imports: [
    CreateQuoteForm,
    CreateQuoteFormSignal,
    ExploreView,
    AllQuotesView,
    InterceptorsView,
    RoutingView,
    QuoteManagementView,
    BackgroundJobsView,
    ServiceBusView,
    OutboxView,
    CacheView,
    ResilienceView,
  ],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {
  protected readonly store = inject(QuotesStore);

  // Injected here (root component, always constructed first, regardless of
  // activeTab's initial value) purely so AuthService's constructor - and
  // therefore its one call to handleRedirectObservable() - runs
  // unconditionally on every page load. Harmless while authEnabled is
  // false (nothing ever starts a redirect, so there's nothing for it to
  // find), but a real bug this fixed once auth was actually enabled:
  // AuthService was only ever injected by RoutingView/LoginRoute, both of
  // which live inside the 'routing' tab; a loginRedirect() always lands
  // back on bare `redirectUri` ('/'), which activeTab's own logic below
  // resolves to the 'explore' tab, not 'routing' - so AuthService was never
  // constructed on that page load, handleRedirectObservable() never ran, no
  // account was ever set, and clicking back into a guarded route just
  // started ANOTHER loginRedirect() - an infinite "keeps asking to log in"
  // loop with no error, confirmed live in the browser before this fix.
  protected readonly authService = inject(AuthService);

  // Defaults to 'explore', EXCEPT a direct/reloaded deep link into the
  // router's own URLs (/quotes, /quotes/:id, /login) - without this, the
  // router-outlet (which lives inside the 'routing' tab) wouldn't be in the
  // DOM yet on a fresh load, so a reload on /quotes/17 would silently show
  // the Explore tab instead of the quote the URL points at.
  //
  // Reads `location.pathname`, NOT `Router.url` - the first draft read
  // `Router.url` here and it was still '/' at this point, every time,
  // because the router's initial navigation is asynchronous and hasn't run
  // yet when this constructor executes. Caught live: reloading on
  // /quotes/17 rendered the Explore tab instead of the quote, confirmed
  // with Playwright before switching to `location.pathname`, which reflects
  // the real browser URL immediately.
  protected readonly activeTab = signal<Tab>(
    location.pathname.startsWith('/quotes') || location.pathname.startsWith('/login') ? 'routing' : 'explore',
  );

  constructor() {
    this.store.start();

    // A loginRedirect() started from the 'routing' tab always lands back on
    // bare `redirectUri`, which the signal above resolves to 'explore' (no
    // `/quotes`/`/login` path survives the round trip to Entra ID and back) -
    // without this, a successful sign-in would silently strand the user on
    // Explore instead of returning them to where they were. Reads
    // `justSignedIn`, NOT `isAuthenticated` - the latter is also true on a
    // plain reload with an already-active session from before, which would
    // yank the user back to 'routing' every time they reload on Explore,
    // not just right after an actual login. A no-op while authEnabled is
    // false: justSignedIn can only become true via a real redirect
    // completing, which nothing triggers yet.
    effect(() => {
      if (this.authService.justSignedIn()) {
        this.activeTab.set('routing');
      }
    });
  }

  protected setTab(tab: Tab): void {
    this.activeTab.set(tab);
  }

  protected onQuoteCreated(quote: Quote): void {
    this.store.onQuoteCreated(quote);
    this.activeTab.set('explore');
  }
}
