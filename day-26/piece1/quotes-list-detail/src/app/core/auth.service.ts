import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { MsalBroadcastService, MsalService } from '@azure/msal-angular';
import { EventType, type AccountInfo, type EventMessage } from '@azure/msal-browser';
import { filter } from 'rxjs';
import { environment } from '../../environments/environment';

/**
 * Day 25: real Entra ID sign-in, feature-flagged off for now (see
 * environment.ts's `authEnabled`) - this service, MsalGuard, and
 * MsalInterceptor are all still fully wired (see app.config.ts), just inert
 * while disabled: MsalGuard isn't applied to any route (quotes.routes.ts)
 * and MsalInterceptor's protectedResourceMap is empty (app.config.ts), so
 * nothing ever triggers an interactive login. login()/logout() here are
 * real MSAL calls, only reachable once the login route/button exist again
 * (gated the same way).
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly msal = inject(MsalService);
  private readonly broadcast = inject(MsalBroadcastService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly account = signal<AccountInfo | null>(this.msal.instance.getActiveAccount());
  readonly isAuthenticated = computed(() => this.account() !== null);
  readonly accountName = computed(() => this.account()?.name ?? this.account()?.username ?? null);

  // True exactly once per redirect round trip, only when handleRedirectObservable
  // actually consumed a real response from Entra ID - NOT true just because an
  // account happens to already be active from a previous session. App reads this
  // (not isAuthenticated) to decide whether to restore the 'routing' tab after
  // login - see app.ts for why that distinction matters.
  readonly justSignedIn = signal(false);

  constructor() {
    // Harmless while authEnabled is false: nothing ever calls loginRedirect(),
    // so there's no pending redirect response for this to find - `result` is
    // just null every time, same as any other normal page load. Kept
    // unconditional (not gated on authEnabled) so it's already correct and
    // in place for the moment authEnabled flips to true - one less thing to
    // remember to re-wire then.
    this.msal.handleRedirectObservable().subscribe((result) => {
      if (result?.account) {
        this.msal.instance.setActiveAccount(result.account);
        this.account.set(result.account);
        this.justSignedIn.set(true);
      }
    });

    // msal-browser does not automatically set the "active" account after a
    // successful interactive login - LOGIN_SUCCESS/ACQUIRE_TOKEN_SUCCESS is
    // the documented signal to do that explicitly. Kept as a second path
    // (distinct from the handleRedirectObservable result above) because it
    // also covers silent token acquisitions that change the active account
    // outside of a fresh login - it deliberately does NOT set `justSignedIn`.
    const subscription = this.broadcast.msalSubject$
      .pipe(
        filter(
          (message: EventMessage) =>
            message.eventType === EventType.LOGIN_SUCCESS || message.eventType === EventType.ACQUIRE_TOKEN_SUCCESS,
        ),
      )
      .subscribe((message) => {
        const account = (message.payload as { account?: AccountInfo } | null)?.account;
        if (account) {
          this.msal.instance.setActiveAccount(account);
          this.account.set(account);
        }
      });

    this.destroyRef.onDestroy(() => subscription.unsubscribe());
  }

  login(): void {
    this.msal.loginRedirect({ scopes: [environment.msal.apiScope] }).subscribe();
  }

  logout(): void {
    this.msal.logoutRedirect().subscribe();
  }
}
