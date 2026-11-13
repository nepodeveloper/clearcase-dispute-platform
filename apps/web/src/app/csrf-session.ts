import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class CsrfSession {
  private readonly token = signal<string | null>(null);
  readonly csrfToken = this.token.asReadonly();
  update(token: string): void { this.token.set(token); }
  clear(): void { this.token.set(null); }
}
