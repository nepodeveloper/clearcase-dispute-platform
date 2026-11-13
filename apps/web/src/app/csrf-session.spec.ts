import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CsrfSession } from './csrf-session';

describe('CsrfSession', () => {
  let http: HttpTestingController;
  let session: CsrfSession;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    session = TestBed.inject(CsrfSession);
  });

  afterEach(() => http.verify());

  it('loads csrf token from /api/v1/auth/me', async () => {
    const initialization = session.initialize();
    const request = http.expectOne('/api/v1/auth/me');
    expect(request.request.method).toBe('GET');
    request.flush({ csrfToken: 'token-from-me' });
    await initialization;

    expect(session.csrfToken()).toBe('token-from-me');
  });

  it('clears token when session endpoint fails', async () => {
    const initialization = session.initialize();
    const request = http.expectOne('/api/v1/auth/me');
    request.flush({}, { status: 401, statusText: 'Unauthorized' });
    await initialization;

    expect(session.csrfToken()).toBeNull();
  });
});
