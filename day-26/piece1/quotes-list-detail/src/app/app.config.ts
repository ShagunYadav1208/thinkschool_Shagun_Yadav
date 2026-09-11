import {
  ApplicationConfig,
  importProvidersFrom,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { HTTP_INTERCEPTORS, provideHttpClient, withInterceptors, withInterceptorsFromDi } from '@angular/common/http';
import { provideRouter, withComponentInputBinding, withViewTransitions } from '@angular/router';
import { InteractionType, PublicClientApplication } from '@azure/msal-browser';
import {
  MSAL_GUARD_CONFIG,
  MSAL_INSTANCE,
  MSAL_INTERCEPTOR_CONFIG,
  MsalInterceptor,
  MsalModule,
  type MsalGuardConfiguration,
  type MsalInterceptorConfiguration,
} from '@azure/msal-angular';
import { environment } from '../environments/environment';
import { errorMappingInterceptor } from './core/error-mapping.interceptor';
import { retryInterceptor } from './core/retry.interceptor';
import { quotesRoutes } from './quotes-routing/quotes.routes';

// Day 25: real Entra ID sign-in, feature-flagged off for now
// (environment.ts's `authEnabled`) - see README "Entra ID app auth
// (feature-flagged, off by default)". clientId/tenantId are public
// identifiers, not secrets - see README "Why these IDs are safe to commit."
//
// MSAL's own providers below are ALWAYS registered, even while disabled -
// only their BEHAVIOR is gated (empty protectedResourceMap, no MsalGuard on
// any route - see quotes.routes.ts). Angular DI has no "provide this only
// if some runtime condition holds" - a component that injects MsalService
// while its provider doesn't exist throws NG0201, so the safe way to keep
// this switchable without a second migration later is: always provide,
// never force interaction while off.
//
// Memoized on purpose - a real bug this caught live: `msalInstanceFactory()`
// is invoked from TWO places below (once as MsalModule.forRoot()'s direct
// argument, once via the `MSAL_INSTANCE` provider's `useFactory`). Without
// this cache, each call built a SEPARATE `new PublicClientApplication(...)`,
// so `MsalGuard`/`MsalInterceptor` (which resolve `MSAL_INSTANCE` through
// Angular DI) could end up talking to a different in-memory instance than
// whichever one actually handled the redirect back from Entra ID - no
// thrown error, just a silent "no account found" on that OTHER instance,
// which immediately fired another loginRedirect(). Confirmed live: same
// browser tab throughout, zero console errors, an infinite login loop -
// exactly what two disjoint PublicClientApplication instances produces.
let msalInstanceSingleton: PublicClientApplication | undefined;

// Armed for exactly one redirect by AuthService.logout() (via
// suppressNextRedirectNavigation() below), then immediately disarmed the
// instant onRedirectNavigate below actually consults it - so it only ever
// affects the ONE logoutRedirect() call that armed it, never a later
// loginRedirect(). Needed because msal-browser reads onRedirectNavigate for
// BOTH login and logout from this same PublicClientApplication config
// (confirmed live in day-25 by reading RedirectClient's source -
// EndSessionRequest has no per-call equivalent in this version) - a
// permanently-false callback here would silently stop login from ever
// reaching Microsoft too.
let allowNextRedirectNavigation = true;
export function suppressNextRedirectNavigation(): void {
  allowNextRedirectNavigation = false;
}

function msalInstanceFactory(): PublicClientApplication {
  msalInstanceSingleton ??= new PublicClientApplication({
    auth: {
      clientId: environment.msal.clientId,
      authority: environment.msal.authority,
      redirectUri: environment.msal.redirectUri,
      // See AuthService.logout() - a local-only sign-out (not a full
      // Microsoft SSO logout affecting every other tab/site) depends on
      // this returning false for exactly the one redirect that armed it.
      onRedirectNavigate: () => {
        const allow = allowNextRedirectNavigation;
        allowNextRedirectNavigation = true;
        return allow;
      },
    },
    cache: {
      cacheLocation: 'localStorage',
    },
  });

  return msalInstanceSingleton;
}

function msalGuardConfigFactory(): MsalGuardConfiguration {
  return {
    interactionType: InteractionType.Redirect,
    authRequest: { scopes: [environment.msal.apiScope] },
  };
}

function msalInterceptorConfigFactory(): MsalInterceptorConfiguration {
  // Empty while authEnabled is false - MsalInterceptor consults this map to
  // decide which outgoing requests need a token; nothing in it means every
  // request just passes through untouched, same as if MsalInterceptor
  // weren't registered at all. This (not removing the provider) is what
  // "off by default" actually means for the interceptor specifically.
  if (!environment.authEnabled) {
    return { interactionType: InteractionType.Redirect, protectedResourceMap: new Map() };
  }

  // Both keys point at the same API - '/api/*' matches the dev-server-proxied
  // relative calls (see environment.ts), the absolute one matches production's
  // direct cross-origin calls (see environment.prod.ts). Only one shape is
  // ever actually in play per environment; keeping both costs nothing.
  const protectedResourceMap = new Map<string, Array<string>>([['/api/*', [environment.msal.apiScope]]]);

  if (environment.apiOrigin) {
    protectedResourceMap.set(`${environment.apiOrigin}/api/*`, [environment.msal.apiScope]);
  }

  return {
    interactionType: InteractionType.Redirect,
    protectedResourceMap,
  };
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    importProvidersFrom(MsalModule.forRoot(msalInstanceFactory(), msalGuardConfigFactory(), msalInterceptorConfigFactory())),
    { provide: MSAL_INSTANCE, useFactory: msalInstanceFactory },
    { provide: MSAL_GUARD_CONFIG, useFactory: msalGuardConfigFactory },
    { provide: MSAL_INTERCEPTOR_CONFIG, useFactory: msalInterceptorConfigFactory },
    // msal-browser 3.x+ (this app is on 5.x) requires an explicit async
    // initialize() before ANY other PublicClientApplication API is called -
    // calling loginRedirect()/handleRedirectObservable() etc. beforehand
    // throws BrowserAuthError: uninitialized_public_client_application.
    // Confirmed live in day-25: real sign-in got all the way through Entra
    // ID's consent screen and back, then failed here because nothing had
    // ever awaited this. provideAppInitializer blocks bootstrap until it
    // resolves, so by the time AuthService's constructor runs (and calls
    // handleRedirectObservable()), the instance is guaranteed ready.
    provideAppInitializer(() => inject(MSAL_INSTANCE).initialize()),
    // MsalInterceptor is DI-based (HTTP_INTERCEPTORS), not the functional
    // `withInterceptors` style the other two use - withInterceptorsFromDi()
    // is what makes provideHttpClient still pick it up. It attaches a real,
    // silently-acquired (or interactively re-acquired, if needed) Entra ID
    // access token to every request matching protectedResourceMap above -
    // an empty map while authEnabled is false, so this is a no-op for now.
    { provide: HTTP_INTERCEPTORS, useClass: MsalInterceptor, multi: true },
    // Order matters: requests flow msal -> errorMapping -> retry -> backend;
    // responses flow the other way, so retryInterceptor sees the raw
    // HttpErrorResponse first (and can retry on it), and only once it gives
    // up does errorMappingInterceptor turn that into an AppHttpError.
    provideHttpClient(withInterceptors([errorMappingInterceptor, retryInterceptor]), withInterceptorsFromDi()),
    // withComponentInputBinding binds the `:id` segment straight onto
    // QuoteDetailRoute's `id` input; withViewTransitions wraps every
    // navigation in document.startViewTransition() so the shared
    // view-transition-name on a quote's title actually morphs.
    provideRouter(quotesRoutes, withComponentInputBinding(), withViewTransitions()),
  ],
};
