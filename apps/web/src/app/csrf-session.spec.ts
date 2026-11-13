import { DOCUMENT } from '@angular/common';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { CsrfSession } from './csrf-session';
import { sessionInterceptor } from './auth/session-interceptor';
import { AuthService } from './auth/auth-service';
import { DisputeApi } from './disputes/dispute-api';

describe('CSRF session interceptor', () => {
  let http: HttpTestingController;
  let client: HttpClient;
  let csrf: CsrfSession;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(withInterceptors([sessionInterceptor])),
      provideHttpClientTesting(), provideRouter([]),
    ] });
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
    csrf = TestBed.inject(CsrfSession);
    csrf.update('session-token');
  });
  afterEach(() => http.verify());

  for (const method of ['POST', 'PUT', 'PATCH', 'DELETE']) {
    it(`attaches CSRF to same-origin API ${method}, including absolute URLs and legacy aliases`, () => {
      for (const url of ['/api/v1/disputes', '/v1/disputes',
        `${TestBed.inject(DOCUMENT).location.origin}/api/v1/auth/logout`]) {
        client.request(method, url, { body: {} }).subscribe();
        const request = http.expectOne(url);
        expect(request.request.headers.get('X-CSRF-TOKEN')).toBe('session-token');
        expect(request.request.headers.has('X-Correlation-ID')).toBe(true);
        request.flush({});
      }
    });
  }

  it('does not send CSRF to external origins, non-API URLs, safe methods or login', () => {
    for (const url of ['https://external.invalid/api/v1/disputes', '//external.invalid/api/v1/disputes',
      '/assets/example', '/api/v1/auth/login']) {
      client.post(url, {}).subscribe();
      const request = http.expectOne(url);
      expect(request.request.headers.has('X-CSRF-TOKEN')).toBe(false);
      request.flush({});
    }
    client.get('/api/v1/auth/me').subscribe();
    const request = http.expectOne('/api/v1/auth/me');
    expect(request.request.headers.has('X-CSRF-TOKEN')).toBe(false);
    request.flush({});
  });

  it('protects existing dispute submission without altering its payload', () => {
    const payload = { transactionId: 'transaction-id', reasonCode: 'duplicateCharge', description: 'Please investigate this charge.' };
    TestBed.inject(DisputeApi).submit(payload).subscribe();
    const request = http.expectOne('/api/v1/disputes');
    expect(request.request.body).toEqual(payload);
    expect(request.request.headers.get('X-CSRF-TOKEN')).toBe('session-token');
    request.flush({});
  });

  it('clears a failed authenticated session, redirects to login, and propagates the HTTP error', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    let status: number | undefined;
    client.get('/api/v1/transactions').subscribe({ error: error => status = error.status });
    http.expectOne('/api/v1/transactions').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(status).toBe(401);
    expect(csrf.csrfToken()).toBeNull();
    expect(TestBed.inject(AuthService).user()).toBeNull();
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/' } });
  });

  it('does not clear the session when an external service returns 401', () => {
    client.get('https://external.invalid/api/v1/auth/me').subscribe({ error: () => undefined });
    http.expectOne('https://external.invalid/api/v1/auth/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(csrf.csrfToken()).toBe('session-token');
  });

  it('preserves correlation and ignores a late 401 belonging to an older session', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    client.get('/api/v1/transactions', { headers: { 'X-Correlation-ID': 'existing-correlation' } })
      .subscribe({ error: () => undefined });
    const request = http.expectOne('/api/v1/transactions');
    expect(request.request.headers.get('X-Correlation-ID')).toBe('existing-correlation');
    csrf.update('new-session-token');
    request.flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(csrf.csrfToken()).toBe('new-session-token');
    expect(navigate).not.toHaveBeenCalled();
  });
});
