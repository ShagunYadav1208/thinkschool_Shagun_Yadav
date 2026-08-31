import { Component, inject } from '@angular/core';
import { QuotesStore } from '../quotes-store';

/**
 * A plain, unfiltered overview of every quote - full text, no truncation,
 * no click-through detail fetch. Deliberately simpler than Explore (which
 * has search/filter + a separate detail pane): this tab is for skimming
 * everything at once.
 */
@Component({
  selector: 'app-all-quotes-view',
  standalone: true,
  imports: [],
  templateUrl: './all-quotes-view.html',
  styleUrl: './all-quotes-view.css',
})
export class AllQuotesView {
  protected readonly store = inject(QuotesStore);
}
