import { TestBed } from '@angular/core/testing';
import { Router, UrlTree, provideRouter } from '@angular/router';
import { authGuard } from './auth.guard';
import { AuthService } from './auth.service';

/**
 * Pins the guard's actual contract: `true` when authenticated, and
 * SPECIFICALLY a `UrlTree` (not a bare `false`) when it isn't - a bare
 * `false` blocks the navigation but never tells the router where to go,
 * which is the mistake this test would have caught in the first draft.
 */
describe('authGuard', () => {
  let authService: AuthService;

  beforeEach(() => {
    localStorage.removeItem('auth_token');
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    authService = TestBed.inject(AuthService);
  });

  it('allows navigation when authenticated', () => {
    authService.login();
    const result = TestBed.runInInjectionContext(() => authGuard({} as never, {} as never));
    expect(result).toBe(true);
  });

  it('redirects to /login via a UrlTree when unauthenticated', () => {
    const result = TestBed.runInInjectionContext(() => authGuard({} as never, {} as never));
    expect(result).not.toBe(false);
    expect(result instanceof UrlTree).toBe(true);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/login');
  });
});
