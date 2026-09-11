import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth.service';

/**
 * Hosts the lazy-loaded quotes/login/detail routes. The router-outlet only
 * exists in the DOM while this tab is active - see App's constructor for
 * how a direct deep link (e.g. reloading on `/quotes/17`) still lands here
 * instead of silently defaulting to the Explore tab.
 */
@Component({
  selector: 'app-routing-view',
  imports: [RouterOutlet],
  templateUrl: './routing-view.html',
  styleUrl: './routing-view.css',
})
export class RoutingView {
  protected readonly authService = inject(AuthService);

  protected logOut(): void {
    this.authService.logout();
  }
}
