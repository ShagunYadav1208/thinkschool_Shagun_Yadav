import { Component } from '@angular/core';
import { QuoteListDetail } from './quote-list-detail/quote-list-detail';

@Component({
  imports: [QuoteListDetail],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {}
