import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { AuthService } from './core/auth.service';

// App eagerly injects AuthService (see app.ts's own comment on why - it's
// what fixed the "keeps asking to log in" bug from before auth was
// feature-flagged off) - the real AuthService needs a real
// MsalService/PublicClientApplication, which is unrelated to what this
// smoke test checks, so a lightweight stub stands in instead of pulling the
// whole MSAL provider graph into a "does this component exist" test.
class AuthServiceStub {
  readonly isAuthenticated = signal(false);
  readonly justSignedIn = signal(false);
  readonly accountName = signal<string | null>(null);
  // authEnabled is now true (matching day-25's current state), so app.html's
  // login wall actually renders in this test - it reads redirectError()
  // unconditionally, so the stub needs it too or the template throws.
  readonly redirectError = signal<string | null>(null);
  login(): void {}
  logout(): void {}
}

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useClass: AuthServiceStub },
      ],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });
});
