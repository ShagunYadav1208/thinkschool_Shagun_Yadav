import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Returning `false` here would block the navigation but leave the URL bar
 * and screen exactly where they were - no redirect actually happens unless
 * the guard hands the router somewhere else to go. A `UrlTree` (or
 * `router.navigate` + `false`) is what makes "redirects when unauthenticated"
 * true instead of just "silently does nothing."
 */
export const authGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  return authService.isAuthenticated() ? true : router.createUrlTree(['/login']);
};
