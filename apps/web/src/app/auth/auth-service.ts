import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { CsrfSession } from '../csrf-session';

export type AuthRole = 'Customer' | 'Agent' | 'Supervisor';
export interface AuthUser {
  username: string;
  actorType: 'customer' | 'bankEmployee';
  roles: AuthRole[];
  customerId?: string | null;
}
export interface AuthSession extends AuthUser { csrfToken: string }

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfSession);
  private readonly identity = signal<AuthUser | null>(null);
  private readonly busy = signal(false);
  private readonly failure = signal('');
  private restoration?: Promise<void>;
  readonly user = this.identity.asReadonly();
  readonly loading = this.busy.asReadonly();
  readonly error = this.failure.asReadonly();

  restore(): Promise<void> {
    return this.restoration ??= this.restoreSession();
  }

  private async restoreSession(): Promise<void> {
    this.busy.set(true);
    try {
      this.accept(await firstValueFrom(this.http.get<AuthSession>('/api/v1/auth/me')));
    } catch (error: unknown) {
      this.expire();
      this.failure.set(error instanceof HttpErrorResponse && error.status === 401 ? '' : authError(error));
    } finally {
      this.busy.set(false);
    }
  }

  async login(username: string, password: string): Promise<boolean> {
    this.busy.set(true);
    this.failure.set('');
    try {
      this.accept(await firstValueFrom(this.http.post<AuthSession>('/api/v1/auth/login', { username, password })));
      this.restoration = Promise.resolve();
      return true;
    } catch (error: unknown) {
      this.expire();
      this.failure.set(authError(error));
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  async logout(): Promise<boolean> {
    this.busy.set(true);
    this.failure.set('');
    try {
      await firstValueFrom(this.http.post<void>('/api/v1/auth/logout', {}));
      this.expire();
      this.failure.set('');
      return true;
    } catch (error: unknown) {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        this.expire();
        return true;
      }
      this.failure.set(authError(error));
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  expire(): void {
    this.identity.set(null);
    this.csrf.clear();
    this.failure.set('Your session has ended. Please sign in again.');
  }

  private accept({ csrfToken, ...user }: AuthSession): void {
    this.identity.set(user);
    this.csrf.update(csrfToken);
    this.failure.set('');
  }
}

function authError(error: unknown): string {
  const status = error instanceof HttpErrorResponse ? error.status : null;
  const message = status === 401 ? 'Username or password is incorrect.'
    : status === 429 ? 'Too many sign-in attempts. Wait a minute before trying again.'
      : 'The authentication service is unavailable. Please try again.';
  const problem: unknown = error instanceof HttpErrorResponse ? error.error : null;
  const correlationId = typeof problem === 'object' && problem !== null && 'correlationId' in problem
    && typeof problem.correlationId === 'string' ? problem.correlationId : null;
  return correlationId === null ? message : `${message} Reference: ${correlationId}`;
}
