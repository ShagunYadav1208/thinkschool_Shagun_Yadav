import { Component, effect, inject, signal } from '@angular/core';
import { AuthService } from './core/auth.service';
import { environment } from '../environments/environment';
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
import { QuoteManagementStore } from './quote-management-store';
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
  private readonly quoteManagementStore = inject(QuoteManagementStore);
  protected readonly authService = inject(AuthService);

  // Read once - reflects app.html's login wall, which hides every tab
  // (Explore included) until this is either false or the user is signed in.
  protected readonly authEnabled = environment.authEnabled;

  // Defaults to 'explore', EXCEPT a direct/reloaded deep link into the
  // router's own URLs (/login, /quotes, /quotes/:id) - without this, the
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
    location.pathname.startsWith('/quotes') || location.pathname.startsWith('/login') ? 'routing' : 'explore'
  );

  constructor() {
    // Gated on sign-in (when authEnabled): app.html doesn't render any tab -
    // Explore included - until the user is signed in, but that alone
    // wouldn't stop THIS call, since it runs here in the root constructor
    // regardless of what the template shows. Without this guard, `start()`
    // fired its `/api/quotes` request immediately on boot, before the user
    // ever saw a login screen - MsalInterceptor caught that request, found
    // no account, and forced an interactive redirect to Microsoft itself, on
    // every single page load. `start()` is idempotent (quotes-store.ts), so
    // it's safe to let this effect re-run past its first true.
    effect(() => {
      if (!this.authEnabled || this.authService.isAuthenticated()) {
        this.store.start();
      }
    });

    // A loginRedirect() started while on the 'routing' tab always lands back
    // on bare `redirectUri`, which the signal above resolves to 'explore' (no
    // `/quotes`/`/login` path survives the round trip to Entra ID and back) -
    // without this, a successful sign-in would silently strand the user on
    // Explore instead of returning them to where they were. Reads
    // `justSignedIn`, NOT `isAuthenticated` - the latter is also true on a
    // plain reload with an already-active session from before, which would
    // yank the user back to 'routing' every time they reload on Explore, not
    // just right after an actual login. A no-op while authEnabled is false:
    // justSignedIn can only become true via a real redirect completing,
    // which nothing triggers yet.
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
    // QuoteManagementStore (the 'manage' tab) keeps its own independent copy
    // of the list, fetched once on start() - it has no other way to learn
    // about a quote created from here, so without this it stayed stale until
    // a full page reload. See QuoteManagementStore.refresh()'s own comment.
    this.quoteManagementStore.refresh();
    this.activeTab.set('explore');
  }
}
