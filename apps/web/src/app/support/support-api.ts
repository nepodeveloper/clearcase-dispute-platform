import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { SKIP_SESSION_EXPIRY } from '../auth/session-interceptor';

export interface SupportContact {
  email: string;
  phone?: string;
  hours: string;
  responseTime: string;
}

@Injectable({ providedIn: 'root' })
export class SupportApi {
  private readonly http = inject(HttpClient);

  /** `anonymous` lookups (sign-in page) must not treat a 401 as the end of a signed-in session. */
  get(anonymous = false) {
    return this.http.get<SupportContact>('/api/v1/support', {
      context: anonymous ? new HttpContext().set(SKIP_SESSION_EXPIRY, true) : undefined,
    });
  }
}
