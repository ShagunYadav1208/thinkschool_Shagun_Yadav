import { Component, inject } from '@angular/core';
import { AuthService } from '../../core/auth.service';

/**
 * Only reachable while environment.ts's `authEnabled` is true - see
 * quotes.routes.ts, which doesn't even register this route otherwise.
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
