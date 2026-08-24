import { Component } from '@angular/core';
import { QuotesFeed } from './quotes-feed/quotes-feed';

@Component({
  imports: [QuotesFeed],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {}
