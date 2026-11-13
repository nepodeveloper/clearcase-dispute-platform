import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { DEMO_HOME, DemoRole, DemoSession } from './demo-session';

@Component({
  selector: 'cc-demo-login',
  template: `
    <section class="auth-layout" aria-labelledby="login-heading">
      <div class="auth-hero">
        <span class="eyebrow-white">Simple. Secure. Transparent.</span>
        <h1 tabindex="-1">Manage disputes<br>with confidence.</h1>
        <p>View transactions, raise a dispute and follow every step &mdash; with a clear view of what happens next.</p>
        <div class="auth-bubbles" aria-hidden="true"><span></span><span></span><span></span></div>
        <div class="hero-footer">ClearCase &middot; Financial dispute management</div>
      </div>
      <form class="auth-form" (submit)="signIn($event)" aria-describedby="demo-login-notice">
        <div class="small-mark"><span class="brand-mark" aria-hidden="true">C</span><strong>ClearCase</strong></div>
        <span class="eyebrow">Welcome back</span>
        <h2 id="login-heading">Sign in to ClearCase</h2>
        <p class="muted">Choose a demo role to explore the experience.</p>
        <div class="cc-field">
          <label for="login-email">Email address <span class="field-hint">(demo only)</span></label>
          <input id="login-email" class="cc-control" value="customer@clearcase.demo" readonly autocomplete="off">
        </div>
        <div class="cc-field">
          <label for="login-password">Password <span class="field-hint">(demo only)</span></label>
          <input id="login-password" class="cc-control" type="password" placeholder="Not used in this preview" readonly autocomplete="off">
        </div>
        <div class="form-row">
          <label class="checkbox"><input type="checkbox" disabled> Remember this device</label>
          <button class="link-button" type="button" (click)="showHelp.set(!showHelp())"
            [attr.aria-expanded]="showHelp()" aria-controls="password-help">Forgot password?</button>
        </div>
        @if (showHelp()) {
          <p class="notice" id="password-help" role="status">No password is needed in this demo.
            Choose a journey below. Nothing is remembered after a page reload.</p>
        }
        <button class="cc-button cc-button--primary full" type="submit" data-testid="demo-customer-login">
          Sign in as customer <span aria-hidden="true">&rarr;</span>
        </button>
        <div class="auth-divider">Preview the other journeys</div>
        <div class="button-row">
          <button class="cc-button cc-button--secondary" type="button" (click)="enter('agent')">Agent view</button>
          <button class="cc-button cc-button--secondary" type="button" (click)="enter('supervisor')">Supervisor view</button>
        </div>
        <div class="security-copy" id="demo-login-notice">UI demo only &mdash; no authentication or API connection.
          These fields are read-only. Never enter real credentials or personal information.</div>
      </form>
    </section>`,
})
export class DemoLoginPage {
  private readonly session = inject(DemoSession);
  private readonly router = inject(Router);
  protected readonly showHelp = signal(false);

  protected signIn(event: Event): void {
    event.preventDefault();
    this.enter('customer');
  }

  protected enter(role: DemoRole): void {
    this.session.enter(role);
    void this.router.navigateByUrl(DEMO_HOME[role]);
  }
}
