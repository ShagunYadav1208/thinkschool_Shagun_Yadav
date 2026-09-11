import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { MsalBroadcastService, MsalService } from '@azure/msal-angular';
import { EventType, type AccountInfo, type EventMessage } from '@azure/msal-browser';
import { filter } from 'rxjs';
import { environment } from '../../environments/environment';
import { suppressNextRedirectNavigation } from '../app.config';

/**
 * Day 25: real Entra ID sign-in. login()/logout() are real MSAL redirect
 * calls - see app.html for the login wall that's the only place login() gets
 * called from, and quotes.routes.ts's authGuard for why the '/quotes/:id'
 * route also checks isAuthenticated() (belt-and-suspenders once the app
 * itself is already gated - see that guard's own header comment).
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

  // Set only when handleRedirectObservable's Observable actually ERRORS -
  // e.g. Entra ID sending back an error instead of a token (AADSTS code +
  // description in `errorMessage`), never just "not signed in yet". Before
  // this existed, an error here had NO subscribe() error handler at all, so
  // RxJS just let it vanish - the user got silently bounced back to the
  // login screen with zero feedback, indistinguishable from having never
  // clicked anything. Read by login-route.html to actually show what went
  // wrong instead of guessing.
  readonly redirectError = signal<string | null>(null);

  constructor() {
    // Unconditional (not gated on authEnabled) so it's already correct and in
    // place for the moment authEnabled flips to true - one less thing to
    // remember to re-wire then. Harmless while disabled: nothing ever calls
    // loginRedirect(), so there's no pending redirect response for this to
    // find - `result` is just null every time, same as any other page load.
    this.msal.handleRedirectObservable().subscribe({
      next: (result) => {
        if (result?.account) {
          this.msal.instance.setActiveAccount(result.account);
          this.account.set(result.account);
          this.justSignedIn.set(true);
          this.redirectError.set(null);
        }
      },
      error: (err: unknown) => {
        const message = err instanceof Error ? err.message : String(err);
        console.error('MSAL redirect error:', err);
        this.redirectError.set(message);
      },
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
    // In this msal-browser version, `onRedirectNavigate` for a LOGOUT is
    // only read from the PublicClientApplication's own config
    // (auth.onRedirectNavigate in app.config.ts), not from a per-call
    // option on logoutRedirect() itself (confirmed by reading
    // RedirectClient's actual source - the EndSessionRequest type doesn't
    // even have this property, which is why a first attempt putting it
    // here didn't compile). That config callback is shared with LOGIN
    // redirects too, so suppressNextRedirectNavigation() arms it for
    // exactly one call - this one - rather than permanently disabling
    // navigation, which would silently break login.
    //
    // Why this matters: a plain logoutRedirect() navigates the browser to
    // Entra ID's end_session_endpoint, which clears the user's Microsoft
    // SSO session everywhere (every other tab/site signed in with the same
    // account), not just this app - reported live as an actual surprise,
    // not a guess. MSAL still does its own local cleanup (this app's
    // cached account/tokens) regardless; only the federated/browser-wide
    // half is what suppressing navigation skips.
    suppressNextRedirectNavigation();
    this.msal.logoutRedirect().subscribe(() => this.account.set(null));
  }
}
