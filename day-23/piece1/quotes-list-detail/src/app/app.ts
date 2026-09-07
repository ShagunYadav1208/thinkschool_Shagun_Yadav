import { Component, inject, signal } from '@angular/core';
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
    this.store.start();
  }

  protected setTab(tab: Tab): void {
    this.activeTab.set(tab);
  }

  protected onQuoteCreated(quote: Quote): void {
    this.store.onQuoteCreated(quote);
    this.activeTab.set('explore');
  }
}
