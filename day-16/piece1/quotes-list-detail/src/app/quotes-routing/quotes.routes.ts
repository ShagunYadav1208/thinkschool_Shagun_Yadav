import { Routes } from '@angular/router';
import { authGuard } from '../core/auth.guard';

/**
 * Each `loadComponent` is its own build chunk, fetched only the first time
 * its route is actually navigated to - confirmed in the Network tab, not
 * just declared here. `quotes/:id` is the only guarded route: the list stays
 * public, viewing one quote's detail requires being "logged in" (see
 * AuthService/authGuard).
 */
export const quotesRoutes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'quotes' },
  {
    path: 'login',
    loadComponent: () => import('./login-route/login-route').then((m) => m.LoginRoute),
  },
  {
    path: 'quotes',
    loadComponent: () => import('./quotes-list-route/quotes-list-route').then((m) => m.QuotesListRoute),
  },
  {
    path: 'quotes/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./quote-detail-route/quote-detail-route').then((m) => m.QuoteDetailRoute),
  },
];
