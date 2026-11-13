import { Component, ElementRef, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from './auth-service';
import { loginDestination } from './auth-guards';

@Component({
  selector: 'cc-login',
  imports: [ReactiveFormsModule],
  template: `
    <section class="auth-layout" aria-labelledby="login-heading">
      <div class="auth-hero">
        <span class="eyebrow-white">Simple. Secure. Transparent.</span>
        <h1 tabindex="-1">Manage disputes<br>with confidence.</h1>
        <p>View transactions, raise a dispute and follow every step &mdash; with a clear view of what happens next.</p>
        <div class="auth-bubbles" aria-hidden="true"><span></span><span></span><span></span></div>
        <div class="hero-footer">ClearCase &middot; Financial dispute management</div>
      </div>
      <form class="auth-form" [formGroup]="form" (ngSubmit)="submit()" novalidate
        aria-describedby="login-notice" [attr.aria-busy]="auth.loading()">
        <div class="small-mark"><span class="brand-mark" aria-hidden="true">C</span><strong>ClearCase</strong></div>
        <h2 id="login-heading">Sign in to ClearCase</h2>
        <p>Your workspace is assigned by your authenticated account.</p>
        <div class="cc-field">
          <label for="username">Username</label>
          <input id="username" class="cc-control" formControlName="username" autocomplete="username" required
            [attr.aria-invalid]="invalid('username')" [attr.aria-describedby]="invalid('username') ? 'username-error' : null">
          @if (invalid('username')) { <span id="username-error" class="cc-form-error">Enter your username.</span> }
        </div>
        <div class="cc-field">
          <label for="password">Password</label>
          <input id="password" class="cc-control" [type]="showPassword() ? 'text' : 'password'"
            formControlName="password" autocomplete="current-password" required
            [attr.aria-invalid]="invalid('password')" [attr.aria-describedby]="invalid('password') ? 'password-error' : null">
          @if (invalid('password')) { <span id="password-error" class="cc-form-error">Enter your password.</span> }
          <button class="cc-button cc-button--secondary" type="button" aria-controls="password"
            [attr.aria-pressed]="showPassword()" (click)="showPassword.set(!showPassword())">
            {{ showPassword() ? 'Hide password' : 'Show password' }}
          </button>
        </div>
        @if (auth.error()) { <p class="cc-form-error" role="alert">{{ auth.error() }}</p> }
        <button class="cc-button cc-button--primary" type="submit" [disabled]="auth.loading()">
          {{ auth.loading() ? 'Signing in...' : 'Sign in' }}
        </button>
        <div class="security-copy" id="login-notice">Local assessment accounts only. No real banking access or money movement.
          Contact your local administrator for access. Never enter real banking credentials.</div>
      </form>
    </section>`,
})
export class LoginPage {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  protected readonly showPassword = signal(false);
  protected readonly form = new FormGroup({
    username: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/\S/)] }),
    password: new FormControl('', { nonNullable: true, validators: Validators.required }),
  });

  protected invalid(field: 'username' | 'password'): boolean {
    return this.form.controls[field].touched && this.form.controls[field].invalid;
  }

  protected async submit(): Promise<void> {
    if (this.auth.loading()) return;
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      this.element.nativeElement.querySelector<HTMLElement>(
        this.form.controls.username.invalid ? '#username' : '#password')?.focus();
      return;
    }
    const { username, password } = this.form.getRawValue();
    const signedIn = await this.auth.login(username.trim(), password);
    this.form.controls.password.reset();
    this.showPassword.set(false);
    const user = this.auth.user();
    if (signedIn && user !== null) {
      await this.router.navigateByUrl(loginDestination(user, this.route.snapshot.queryParamMap.get('returnUrl')));
    } else {
      this.element.nativeElement.querySelector<HTMLElement>('#password')?.focus();
    }
  }
}
