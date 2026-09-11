import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * A deliberately simple guard, not the library's own `MsalGuard`. In
 * practice `MsalGuard`'s internal account-check + auto-redirect logic
 * (built around `inProgress$` settling to `InteractionStatus.None`) proved
 * unreliable here: after a real, completed sign-in, clicking into a guarded
 * route sometimes bounced straight back instead of showing the detail - no
 * error, just silently treated as "not authenticated" even though
 * `AuthService.isAuthenticated()` (the same signal every other part of this
 * app trusts) was true the whole time. This guard sidesteps that state
 * machine entirely and just reads the one signal already trusted elsewhere.
 *
 * Also more honest UX: MsalGuard, on "not authenticated", starts an
 * interactive Microsoft redirect ITSELF. This guard instead sends the user
 * to our own `/login` route, where "Log in with Microsoft" is a real,
 * visible button they click themselves - by the time anyone reaches this
 * guard the app-level login wall (see app.html) has already required
 * sign-in, so in practice this is belt-and-suspenders, not the primary
 * gate.
 */
export const authGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  return authService.isAuthenticated() ? true : router.createUrlTree(['/login']);
};
