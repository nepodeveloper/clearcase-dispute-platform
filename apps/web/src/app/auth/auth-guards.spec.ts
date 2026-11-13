import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, provideRouter, Router, RouterStateSnapshot } from '@angular/router';
import { authGuard, loginDestination } from './auth-guards';
import { AuthSession } from './auth-service';

describe('authentication routing', () => {
  let http: HttpTestingController;
  const session: AuthSession = {
    username: 'demo', actorType: 'customer', roles: ['Customer'], csrfToken: 'csrf',
  };
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
    ] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  function guard(role = 'Customer') {
    const route = new ActivatedRouteSnapshot();
    route.data = { role };
    const state = { url: '/disputes?filter=open' } as RouterStateSnapshot;
    return TestBed.runInInjectionContext(() => authGuard(route, state));
  }

  it('waits for restoration before allowing a customer route', async () => {
    let completed = false;
    const result = Promise.resolve(guard()).then(value => { completed = true; return value; });
    await Promise.resolve();
    expect(completed).toBe(false);
    http.expectOne('/api/v1/auth/me').flush(session);
    expect(await result).toBe(true);
  });

  it('preserves a protected return URL for anonymous navigation', async () => {
    const result = guard();
    http.expectOne('/api/v1/auth/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(TestBed.inject(Router).serializeUrl(await result as import('@angular/router').UrlTree))
      .toBe('/login?returnUrl=%2Fdisputes%3Ffilter%3Dopen');
  });

  it('sends an authenticated but unauthorized user to access denied', async () => {
    const result = guard('Supervisor');
    http.expectOne('/api/v1/auth/me').flush(session);
    expect(TestBed.inject(Router).serializeUrl(await result as import('@angular/router').UrlTree))
      .toBe('/unauthorized');
  });

  it('selects the backend role home and rejects external, unknown and unauthorized return URLs', () => {
    expect(loginDestination(session, '/disputes?filter=open')).toBe('/disputes?filter=open');
    for (const url of ['//evil.invalid', '/\\evil.invalid', 'https://evil.invalid', '/%2f%2fevil.invalid',
      '/login', '/unknown', '/agent/escalations', '/disputes(aux:login)']) {
      expect(loginDestination(session, url)).toBe('/');
    }
    expect(loginDestination({ ...session, roles: ['Agent'] }, null)).toBe('/agent/disputes');
    expect(loginDestination({ ...session, roles: ['Supervisor'] }, '/')).toBe('/agent/escalations');
  });
});
