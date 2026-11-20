import { DOCUMENT } from '@angular/common';
import { HttpContextToken, HttpErrorResponse, HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, tap, throwError } from 'rxjs';
import { CsrfSession } from '../csrf-session';
import { AuthService } from './auth-service';
import { SessionTimeoutService } from './session-timeout-service';

/** Requests that are expected to work without a session (for example help text on the sign-in page). */
export const SKIP_SESSION_EXPIRY = new HttpContextToken<boolean>(() => false);

export const sessionInterceptor: HttpInterceptorFn = (request, next) => {
  const location = inject(DOCUMENT).location;
  const url = new URL(request.url, location.href);
  if (url.origin !== location.origin || !/^\/(?:api\/v1|v1)\//.test(url.pathname)) {
    return next(request);
  }
  const csrf = inject(CsrfSession);
  const auth = inject(AuthService);
  const router = inject(Router);
  const timeout = inject(SessionTimeoutService);
  const token = csrf.csrfToken();
  const login = url.pathname === '/api/v1/auth/login';
  let headers = request.headers;
  if (!headers.has('X-Correlation-ID')) headers = headers.set('X-Correlation-ID', crypto.randomUUID());
  if (!login && token !== null && ['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method)) {
    headers = headers.set('X-CSRF-TOKEN', token);
  }
  return next(request.clone({ headers })).pipe(tap(event => {
    if (event instanceof HttpResponse) timeout.recordActivity();
  }), catchError((error: unknown) => {
    // A late response from an old session must not invalidate a newly signed-in user. Requests sent without a
    // session token, or while sign-out is in flight, cannot belong to a session that is still worth redirecting.
    if (error instanceof HttpErrorResponse && error.status === 401 && !login && token !== null
      && token === csrf.csrfToken() && !auth.loading() && !request.context.get(SKIP_SESSION_EXPIRY)) {
      auth.expire();
      // Already on the sign-in page (for example a late response after sign-out): nothing to redirect.
      if (url.pathname !== '/api/v1/auth/me' && !router.url.startsWith('/login')) {
        void router.navigate(['/login'], { queryParams: { returnUrl: router.url } });
      }
    }
    return throwError(() => error);
  }));
};
