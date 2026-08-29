import { Component, inject } from '@angular/core';
import { QuoteManagementStore } from '../quote-management-store';

/**
 * A paginated, deletable view over the real GET /api/quotes/?page&size and
 * DELETE /api/quotes/{id} endpoints, purely to exercise QuoteManagementStore
 * - the signals-first store is the actual deliverable, this is just enough
 * UI to click through every state (loading / error / empty / a page with
 * data) and every edge (guard-adjacent double-click, page navigation).
 */
@Component({
  selector: 'app-quote-management-view',
  imports: [],
  templateUrl: './quote-management-view.html',
  styleUrl: './quote-management-view.css',
})
export class QuoteManagementView {
  protected readonly store = inject(QuoteManagementStore);

  constructor() {
    this.store.start();
  }
}
