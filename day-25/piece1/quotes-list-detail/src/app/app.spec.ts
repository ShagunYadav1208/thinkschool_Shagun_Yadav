import { TestBed } from '@angular/core/testing';
import { importProvidersFrom } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { MsalModule } from '@azure/msal-angular';
import { InteractionType, PublicClientApplication } from '@azure/msal-browser';
import { environment } from '../environments/environment';
import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    // App root injects AuthService, which injects MsalService/MsalBroadcastService -
    // those only exist once MsalModule.forRoot() has been imported somewhere in the
    // testing module, same as app.config.ts does for the real app. Constructing a
    // real PublicClientApplication here is safe and makes no network call - it's
    // just an in-memory client, same as production until loginRedirect() is
    // actually invoked (which this smoke test never does).
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        importProvidersFrom(
          MsalModule.forRoot(
            new PublicClientApplication({ auth: { clientId: environment.msal.clientId } }),
            { interactionType: InteractionType.Redirect },
            { interactionType: InteractionType.Redirect, protectedResourceMap: new Map() },
          ),
        ),
      ],
    }).compileComponents();
  });

  it('should create the app', async () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();

    // AuthService's constructor calls handleRedirectObservable(), which
    // resolves asynchronously (msal-browser's own internal storage/crypto
    // access takes more than one microtask) - without waiting for it here,
    // it settles AFTER this test function returns and TestBed has already
    // torn down the injector, throwing NG0205 as an unhandled rejection
    // vitest reports against a later, unrelated test file. A single
    // Promise.resolve() wasn't enough (confirmed live); a real macrotask
    // tick is.
    await new Promise((resolve) => setTimeout(resolve, 50));
  });
});
