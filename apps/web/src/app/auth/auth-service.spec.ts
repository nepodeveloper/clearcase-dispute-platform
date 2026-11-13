import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService, AuthSession } from './auth-service';
import { sessionInterceptor } from './session-interceptor';
import { CsrfSession } from '../csrf-session';

export const customerSession: AuthSession = {
  username: 'customer.demo', actorType: 'customer', roles: ['Customer'],
  customerId: '11111111-1111-1111-1111-111111111111', csrfToken: 'customer-csrf',
};

describe('AuthService', () => {
  let auth: AuthService;
  let http: HttpTestingController;
  let csrf: CsrfSession;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(withInterceptors([sessionInterceptor])),
      provideHttpClientTesting(), provideRouter([]),
    ] });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
    csrf = TestBed.inject(CsrfSession);
  });
  afterEach(() => http.verify());

  for (const role of ['Customer', 'Agent', 'Supervisor'] as const) {
    it(`signs in as ${role} using only backend roles`, async () => {
      const login = auth.login('demo', 'temporary-input');
      const request = http.expectOne('/api/v1/auth/login');
      expect(request.request.body).toEqual({ username: 'demo', password: 'temporary-input' });
      expect(request.request.headers.has('X-CSRF-TOKEN')).toBe(false);
      expect(auth.loading()).toBe(true);
      request.flush({ ...customerSession, roles: [role] });
      expect(await login).toBe(true);
      expect(auth.user()?.roles).toEqual([role]);
      expect(csrf.csrfToken()).toBe('customer-csrf');
      expect(auth.loading()).toBe(false);
    });
  }

  it('restores a session once and waits for the response', async () => {
    const first = auth.restore();
    const second = auth.restore();
    expect(auth.user()).toBeNull();
    expect(auth.loading()).toBe(true);
    http.expectOne('/api/v1/auth/me').flush(customerSession);
    await Promise.all([first, second]);
    expect(auth.user()?.username).toBe('customer.demo');
    expect(csrf.csrfToken()).toBe('customer-csrf');
    await auth.restore();
    http.expectNone('/api/v1/auth/me');
  });

  for (const [status, message] of [[401, 'Username or password'], [429, 'Too many'], [503, 'unavailable']] as const) {
    it(`handles login ${status} without retaining a token`, async () => {
      csrf.update('stale-token');
      const login = auth.login('demo', 'temporary-input');
      http.expectOne('/api/v1/auth/login').flush(
        { code: 'auth.error', correlationId: 'login-correlation' }, { status, statusText: 'Error' });
      expect(await login).toBe(false);
      expect(auth.error()).toContain(message);
      expect(auth.error()).toContain('login-correlation');
      expect(auth.user()).toBeNull();
      expect(csrf.csrfToken()).toBeNull();
    });
  }

  it('clears expired sessions and reports restoration outages distinctly', async () => {
    const restore = auth.restore();
    http.expectOne('/api/v1/auth/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    await restore;
    expect(auth.user()).toBeNull();
    expect(csrf.csrfToken()).toBeNull();
    expect(auth.error()).toBe('');
  });

  it('reports a restoration outage instead of pretending restoration succeeded', async () => {
    const restore = auth.restore();
    http.expectOne('/api/v1/auth/me').flush({}, { status: 503, statusText: 'Unavailable' });
    await restore;
    expect(auth.error()).toContain('unavailable');
    expect(auth.loading()).toBe(false);
  });

  it('signs out with CSRF and clears identity and token only after server acknowledgement', async () => {
    const restore = auth.restore();
    http.expectOne('/api/v1/auth/me').flush(customerSession);
    await restore;
    const logout = auth.logout();
    const request = http.expectOne('/api/v1/auth/logout');
    expect(request.request.headers.get('X-CSRF-TOKEN')).toBe('customer-csrf');
    expect(auth.user()).not.toBeNull();
    request.flush(null, { status: 204, statusText: 'No Content' });
    expect(await logout).toBe(true);
    expect(auth.user()).toBeNull();
    expect(csrf.csrfToken()).toBeNull();
  });

  it('keeps the session and exposes failed logout for retry', async () => {
    const restore = auth.restore();
    http.expectOne('/api/v1/auth/me').flush(customerSession);
    await restore;
    const logout = auth.logout();
    http.expectOne('/api/v1/auth/logout').flush({}, { status: 503, statusText: 'Unavailable' });
    expect(await logout).toBe(false);
    expect(auth.user()).not.toBeNull();
    expect(auth.error()).toContain('unavailable');
  });
});
