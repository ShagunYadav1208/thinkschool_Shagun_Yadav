import { Component, inject } from '@angular/core';
import { AuthService } from '../../core/auth.service';

/**
 * Only reachable while environment.ts's `authEnabled` is true - see
 * quotes.routes.ts, which doesn't even register this route otherwise. In
 * practice also only reachable already-signed-in, since app.html's
 * whole-app login wall handles the unauthenticated case before any route
 * (this one included) ever mounts - kept as its own route anyway so
 * authGuard has somewhere real to send an unauthenticated deep link, and so
 * logout has a dedicated place to live.
 */
@Component({
  selector: 'app-login-route',
  imports: [],
  templateUrl: './login-route.html',
  styleUrl: './login-route.css',
})
export class LoginRoute {
  protected readonly authService = inject(AuthService);

  protected logIn(): void {
    this.authService.login();
  }

  protected logOut(): void {
    this.authService.logout();
  }
}
