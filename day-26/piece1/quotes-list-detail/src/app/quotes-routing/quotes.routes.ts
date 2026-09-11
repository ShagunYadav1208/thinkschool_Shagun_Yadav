import { Routes } from '@angular/router';
import { authGuard } from '../core/auth.guard';
import { environment } from '../../environments/environment';

/**
 * Each `loadComponent` is its own build chunk, fetched only the first time
 * its route is actually navigated to - confirmed in the Network tab, not
 * just declared here. `quotes/:id` carries `authGuard` only while
 * `environment.authEnabled` is true (see README "Entra ID app auth
 * (feature-flagged, off by default)") - while false, neither the `/login`
 * route nor `authGuard` are registered at all. When enabled, the real
 * primary gate is app.html's whole-app login wall (nothing here renders
 * until signed in); this route-level guard is belt-and-suspenders for a
 * direct/reloaded deep link into `/quotes/:id`.
 */
export const quotesRoutes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'quotes' },
  ...(environment.authEnabled
    ? [
        {
          path: 'login',
          loadComponent: () => import('./login-route/login-route').then((m) => m.LoginRoute),
        },
      ]
    : []),
  {
    path: 'quotes',
    loadComponent: () => import('./quotes-list-route/quotes-list-route').then((m) => m.QuotesListRoute),
  },
  {
    path: 'quotes/:id',
    canActivate: environment.authEnabled ? [authGuard] : [],
    loadComponent: () => import('./quote-detail-route/quote-detail-route').then((m) => m.QuoteDetailRoute),
  },
];
