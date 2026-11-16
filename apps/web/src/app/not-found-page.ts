import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from './auth/auth-service';
import { homeUrl } from './auth/auth-guards';

@Component({
  selector: 'cc-not-found',
  imports: [RouterLink],
  template: `
    <section class="cc-empty" aria-labelledby="not-found-heading">
      <h1 id="not-found-heading" tabindex="-1">Page not found</h1>
      <p>We could not find that page. Check the address or return to your workspace.</p>
      <a class="cc-button cc-button--primary" [routerLink]="home()">
        {{ auth.user() === null ? 'Go to sign in' : 'Return to your workspace' }}
      </a>
    </section>`,
})
export class NotFoundPage {
  protected readonly auth = inject(AuthService);
  protected readonly home = computed(() => {
    const user = this.auth.user();
    return user === null ? '/login' : homeUrl(user);
  });

  constructor() { void this.auth.restore(); }
}
