import { Injectable, signal } from '@angular/core';

const TOKEN_STORAGE_KEY = 'auth_token';

/**
 * The real Week-1 QuotesApi has no auth of its own (day-15 already
 * established this), so there's no login endpoint to call. This is a
 * client-only "logged in" flag, backed by the SAME localStorage key
 * authInterceptor reads - logging out here is a real, observable change to
 * the Authorization header on the next request, not two disconnected fakes.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  readonly isAuthenticated = signal(!!localStorage.getItem(TOKEN_STORAGE_KEY));

  login(): void {
    localStorage.setItem(TOKEN_STORAGE_KEY, 'demo-token');
    this.isAuthenticated.set(true);
  }

  logout(): void {
    localStorage.removeItem(TOKEN_STORAGE_KEY);
    this.isAuthenticated.set(false);
  }
}
