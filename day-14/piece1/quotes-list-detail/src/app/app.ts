import { Component, inject, signal } from '@angular/core';
import { CreateQuoteForm } from './create-quote-form/create-quote-form';
import { ExploreView } from './explore-view/explore-view';
import { AllQuotesView } from './all-quotes-view/all-quotes-view';
import { QuotesStore } from './quotes-store';
import { Quote } from './models/quote.model';

type Tab = 'explore' | 'create' | 'all';

@Component({
  imports: [CreateQuoteForm, ExploreView, AllQuotesView],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {
  protected readonly store = inject(QuotesStore);
  protected readonly activeTab = signal<Tab>('explore');

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
