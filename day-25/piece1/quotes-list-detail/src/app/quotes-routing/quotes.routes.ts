import { Routes } from '@angular/router';
import { MsalGuard } from '@azure/msal-angular';
import { environment } from '../../environments/environment';

/**
 * Each `loadComponent` is its own build chunk, fetched only the first time
 * its route is actually navigated to - confirmed in the Network tab, not
 * just declared here. `quotes/:id` is the only guarded route, and ONLY
 * while `environment.authEnabled` is true (see README "Entra ID app auth
 * (feature-flagged, off by default)") - while false, neither the `/login`
 * route nor `MsalGuard` are registered at all, so there's nothing in this
 * app that could ever trigger an Entra ID redirect.
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
    canActivate: environment.authEnabled ? [MsalGuard] : [],
    loadComponent: () => import('./quote-detail-route/quote-detail-route').then((m) => m.QuoteDetailRoute),
  },
];
