import { computed, DestroyRef, inject, Injectable, InjectionToken, signal } from '@angular/core';
import { AuthService } from './auth-service';

/** The API session lifetime is sliding: user-initiated API calls extend it, while background checks never do. */
export const sessionLifetimeMs = 30 * 60_000;
export const sessionWarningMs = 5 * 60_000;
const tickIntervalMs = 10_000;

export const sessionClock = new InjectionToken<() => number>('sessionClock', {
  providedIn: 'root',
  factory: () => () => Date.now(),
});

@Injectable({ providedIn: 'root' })
export class SessionTimeoutService {
  private readonly auth = inject(AuthService);
  private readonly clock = inject(sessionClock);
  private readonly lastActivity = signal<number | null>(null);
  private readonly now = signal(this.clock());
  private readonly extending = signal(false);

  readonly remainingMs = computed(() => {
    const last = this.lastActivity();
    if (last === null || this.auth.user() === null) return null;
    return Math.max(0, last + sessionLifetimeMs - this.now());
  });
  readonly warning = computed(() => {
    const remaining = this.remainingMs();
    return remaining !== null && remaining <= sessionWarningMs;
  });
  readonly expiresIn = computed(() => {
    const remaining = this.remainingMs() ?? 0;
    if (remaining <= 60_000) return 'less than a minute';
    const minutes = Math.ceil(remaining / 60_000);
    return `${minutes} minutes`;
  });
  readonly busy = this.extending.asReadonly();

  constructor() {
    const timer = setInterval(() => this.tick(), tickIntervalMs);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  recordActivity(): void {
    this.now.set(this.clock());
    this.lastActivity.set(this.now());
  }

  tick(): void {
    this.now.set(this.clock());
  }

  async extend(): Promise<boolean> {
    if (this.extending()) return true;
    this.extending.set(true);
    try {
      return await this.auth.refresh();
    } finally {
      this.extending.set(false);
    }
  }
}
