import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthService } from './auth-service';
import { LoginPage } from './login-page';

describe('LoginPage', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [LoginPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  }));
  afterEach(() => {
    const http = TestBed.inject(HttpTestingController);
    http.match('/api/v1/support').forEach(request => request.flush({}, { status: 401, statusText: 'Unauthorized' }));
    http.verify();
  });

  it('shows the end-of-session message as a polite status rather than an error', async () => {
    TestBed.inject(AuthService).expire();
    const fixture = TestBed.createComponent(LoginPage);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const notice = element.querySelector('[data-testid="session-ended"]')!;
    expect(notice.getAttribute('role')).toBe('status');
    expect(notice.textContent).toContain('Your session has ended. Please sign in again.');
    expect(notice.classList.contains('cc-form-error')).toBe(false);
    expect(element.querySelector('[role="alert"]')).toBeNull();
  });

  it('still announces a failed sign-in as an error alert', async () => {
    const fixture = TestBed.createComponent(LoginPage);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    for (const [id, value] of [['username', 'demo'], ['password', 'wrong-input']]) {
      const input = element.querySelector<HTMLInputElement>(`#${id}`)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    element.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    TestBed.inject(HttpTestingController).expectOne('/api/v1/auth/login')
      .flush({}, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.querySelector('[role="alert"]')?.textContent).toContain('Username or password is incorrect.');
    expect(element.querySelector('[data-testid="session-ended"]')).toBeNull();
  });

  it('shows support contact details under "Need help signing in?" when they load, and nothing when they do not', async () => {
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(LoginPage);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    http.expectOne('/api/v1/support').flush({
      email: 'support@clearcase.example', hours: 'Mon-Fri 08:00-17:00 SAST', responseTime: 'Within 1 business day' });
    fixture.detectChanges();
    expect(element.querySelector('.support-contact h3')?.textContent).toContain('Need help signing in?');
    expect(element.querySelector('[data-testid="support-email"]')?.textContent).toContain('support@clearcase.example');

    const quiet = TestBed.createComponent(LoginPage);
    await quiet.whenStable();
    http.expectOne('/api/v1/support').flush({}, { status: 401, statusText: 'Unauthorized' });
    quiet.detectChanges();
    expect((quiet.nativeElement as HTMLElement).querySelector('.support-contact')).toBeNull();
    expect((quiet.nativeElement as HTMLElement).querySelector('[role="alert"]')).toBeNull();
  });

  it('validates required fields accessibly and focuses the first invalid input', async () => {
    const fixture = TestBed.createComponent(LoginPage);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    element.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
    expect(element.querySelector('#username')?.getAttribute('aria-invalid')).toBe('true');
    expect(element.textContent).toContain('Enter your password.');
    expect(document.activeElement).toBe(element.querySelector('#username'));
    TestBed.inject(HttpTestingController).expectNone('/api/v1/auth/login');
    element.querySelector<HTMLButtonElement>('button[type="button"]')!.click();
    fixture.detectChanges();
    expect(element.querySelector('#password')?.getAttribute('type')).toBe('text');
  });

  for (const [role, destination] of [['Customer', '/'], ['Agent', '/agent/disputes'], ['Supervisor', '/supervisor/escalations']]) {
    it(`submits the reactive form, clears password and opens the ${role} workspace`, async () => {
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
      const fixture = TestBed.createComponent(LoginPage);
      await fixture.whenStable();
      const element = fixture.nativeElement as HTMLElement;
      for (const [id, value] of [['username', 'demo'], ['password', 'temporary-input']]) {
        const input = element.querySelector<HTMLInputElement>(`#${id}`)!;
        input.value = value;
        input.dispatchEvent(new Event('input'));
      }
      element.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
      fixture.detectChanges();
      expect(element.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(true);
      TestBed.inject(HttpTestingController).expectOne('/api/v1/auth/login').flush({
        username: 'demo', actorType: 'customer', roles: [role], csrfToken: 'csrf',
      });
      await fixture.whenStable();
      await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith(destination));
      expect(element.querySelector<HTMLInputElement>('#password')!.value).toBe('');
      expect(element.querySelector('select')).toBeNull();
    });
  }
});
