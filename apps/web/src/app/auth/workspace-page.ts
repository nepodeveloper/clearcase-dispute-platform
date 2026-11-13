import { Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from './auth-service';
import { homeUrl } from './auth-guards';

@Component({
  selector: 'cc-workspace',
  imports: [RouterLink],
  template: `
    <section class="cc-card cc-card__body" aria-labelledby="workspace-heading">
      <h1 id="workspace-heading" tabindex="-1">{{ denied ? 'Access denied' : title }}</h1>
      <p>{{ denied ? 'Your account does not have permission to access that workspace.'
        : 'You are securely signed in. This workspace is not available yet. No cases or financial actions can be processed here.' }}</p>
      <a class="cc-button cc-button--secondary" [routerLink]="home">Return to your workspace</a>
    </section>`,
})
export class WorkspacePage {
  private readonly route = inject(ActivatedRoute);
  protected readonly denied = this.route.snapshot.data['denied'] === true;
  protected readonly title: string = this.route.snapshot.data['title'];
  private readonly user = inject(AuthService).user();
  protected readonly home = this.user === null ? '/login' : homeUrl(this.user);
}
