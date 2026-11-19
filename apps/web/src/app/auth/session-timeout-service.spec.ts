import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from './auth-service';
import { sessionInterceptor } from './session-interceptor';
import { sessionClock, SessionTimeoutService, sessionLifetimeMs, sessionWarningMs } from './session-timeout-service';

describe('SessionTimeoutService', () => {
  const minute = 60_000;
  let now = 0;
  let http: HttpTestingController;
  let service: SessionTimeoutService;

  beforeEach(async () => {
    now = 1_000_000;
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([sessionInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: sessionClock, useValue: () => now },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(SessionTimeoutService);
    const login = TestBed.inject(AuthService).login('customer.demo', 'temporary-input');
    http.expectOne('/api/v1/auth/login').flush(session('csrf-1'));
    await login;
  });

  afterEach(() => http.verify());

  function session(csrfToken: string) {
    return { username: 'customer.demo', actorType: 'customer', roles: ['Customer'], csrfToken };
  }

  function advance(ms: number): void {
    now += ms;
    service.tick();
  }

  it('starts a 30 minute sliding session on successful API activity without warning', () => {
    expect(service.remainingMs()).toBe(sessionLifetimeMs);
    expect(service.warning()).toBe(false);
    advance(sessionLifetimeMs - sessionWarningMs - 1);
    expect(service.warning()).toBe(false);
  });

  it('warns once five minutes or less remain and reports the time left', () => {
    advance(sessionLifetimeMs - sessionWarningMs);
    expect(service.warning()).toBe(true);
    expect(service.expiresIn()).toBe('5 minutes');
    advance(4 * minute + 30_000);
    expect(service.expiresIn()).toBe('less than a minute');
    advance(10 * minute);
    expect(service.remainingMs()).toBe(0);
  });

  it('slides the window when another API call succeeds', async () => {
    advance(sessionLifetimeMs - minute);
    expect(service.warning()).toBe(true);
    const refreshed = TestBed.inject(AuthService).refresh();
    http.expectOne('/api/v1/auth/me').flush(session('csrf-2'));
    await refreshed;

    expect(service.warning()).toBe(false);
    expect(service.remainingMs()).toBe(sessionLifetimeMs);
  });

  it('does not count failed requests as activity', async () => {
    advance(sessionLifetimeMs - minute);
    const refreshed = service.extend();
    http.expectOne('/api/v1/auth/me').flush({}, { status: 503, statusText: 'Unavailable' });

    expect(await refreshed).toBe(false);
    expect(service.warning()).toBe(true);
  });

  it('refreshes through GET /api/v1/auth/me when the user chooses to stay signed in', async () => {
    advance(sessionLifetimeMs - minute);
    const extended = service.extend();
    expect(service.busy()).toBe(true);
    const request = http.expectOne('/api/v1/auth/me');
    expect(request.request.method).toBe('GET');
    request.flush(session('csrf-2'));

    expect(await extended).toBe(true);
    expect(service.busy()).toBe(false);
    expect(service.warning()).toBe(false);
  });

  it('has no countdown once the user is signed out', () => {
    TestBed.inject(AuthService).expire();
    expect(service.remainingMs()).toBeNull();
    expect(service.warning()).toBe(false);
  });
});
