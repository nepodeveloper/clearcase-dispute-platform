import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface AuthSessionResponse {
  csrfToken?: string;
}

@Injectable({ providedIn: 'root' })
export class CsrfSession {
  private readonly http = inject(HttpClient);
  private token: string | null = null;

  csrfToken(): string | null {
    return this.token;
  }

  async initialize(): Promise<void> {
    try {
      const response = await firstValueFrom(
        this.http.get<AuthSessionResponse>('/api/v1/auth/me'),
      );
      this.token = typeof response.csrfToken === 'string' && response.csrfToken !== ''
        ? response.csrfToken
        : null;
    } catch {
      this.token = null;
    }
  }
}
