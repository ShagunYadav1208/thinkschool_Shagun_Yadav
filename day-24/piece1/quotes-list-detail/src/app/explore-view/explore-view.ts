import { Component, inject } from '@angular/core';
import { QuotesStore } from '../quotes-store';

@Component({
  selector: 'app-explore-view',
  standalone: true,
  imports: [],
  templateUrl: './explore-view.html',
  styleUrl: './explore-view.css',
})
export class ExploreView {
  protected readonly store = inject(QuotesStore);

  protected onSearchInput(value: string): void {
    this.store.onSearchInput(value);
  }

  protected onAuthorChange(value: string): void {
    this.store.onAuthorChange(value);
  }

  protected selectQuote(id: number): void {
    this.store.selectQuote(id);
  }
}
