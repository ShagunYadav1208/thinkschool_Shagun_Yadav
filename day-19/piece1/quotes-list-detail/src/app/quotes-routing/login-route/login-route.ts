import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-login-route',
  imports: [],
  templateUrl: './login-route.html',
  styleUrl: './login-route.css',
})
export class LoginRoute {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  protected logIn(): void {
    this.authService.login();
    this.router.navigateByUrl('/quotes');
  }
}
