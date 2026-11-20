import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthService } from '../auth/auth-service';
import { CsrfSession } from '../csrf-session';
import { SKIP_SESSION_EXPIRY, sessionInterceptor } from '../auth/session-interceptor';
import { SupportContactPanel } from './support-contact';

describe('SupportContactPanel', () => {
  const contact = { email: 'support@clearcase.example', phone: '0800 000 000', hours: 'Mon-Fri 08:00-17:00 SAST', responseTime: 'Within 1 business day' };
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([sessionInterceptor])), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function render(inputs: Record<string, unknown> = {}) {
    const fixture = TestBed.createComponent(SupportContactPanel);
    for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
    fixture.detectChanges();
    return fixture;
  }

  it('shows email prefilled with the case reference, phone, hours and response time', () => {
    const fixture = render({ reference: 'DSP-0A1B2C3D4E5F', heading: '' });
    http.expectOne('/api/v1/support').flush(contact);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const email = root.querySelector<HTMLAnchorElement>('[data-testid="support-email"]')!;
    expect(email.getAttribute('href')).toBe('mailto:support@clearcase.example?subject=ClearCase%20support%20request%20-%20DSP-0A1B2C3D4E5F');
    expect(root.querySelector('[data-testid="support-phone"]')?.getAttribute('href')).toBe('tel:0800000000');
    expect(root.textContent).toContain('Mon-Fri 08:00-17:00 SAST');
    expect(root.textContent).toContain('Within 1 business day');
    expect(root.textContent).toContain('Quote case reference DSP-0A1B2C3D4E5F');
    expect(root.querySelector('h3')).toBeNull();
  });

  it('omits the phone line when none is configured', () => {
    const fixture = render();
    http.expectOne('/api/v1/support').flush({ ...contact, phone: undefined });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="support-phone"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('h3')?.textContent).toContain('Contact support');
  });

  it('falls back to plain text with a retry when support details cannot be loaded', () => {
    const fixture = render();
    http.expectOne('/api/v1/support').flush({}, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[role="status"]')?.textContent).toContain('unavailable right now');
    root.querySelector<HTMLButtonElement>('[data-testid="support-retry"]')!.click();
    http.expectOne('/api/v1/support').flush(contact);
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="support-email"]')).not.toBeNull();
    expect(root.querySelector('[data-testid="support-retry"]')).toBeNull();
  });

  it('fails silently in quiet mode and never treats an anonymous 401 as an expired session', () => {
    const expire = vi.spyOn(TestBed.inject(AuthService), 'expire');
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate');
    const fixture = render({ quiet: true, anonymous: true });
    const request = http.expectOne('/api/v1/support');
    expect(request.request.context.get(SKIP_SESSION_EXPIRY)).toBe(true);
    request.flush({}, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent?.trim()).toBe('');
    expect(expire).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
  });

  it('still expires the session for a normal 401 from the same endpoint', () => {
    TestBed.inject(CsrfSession).update('session-token');
    const expire = vi.spyOn(TestBed.inject(AuthService), 'expire');
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = render();
    http.expectOne('/api/v1/support').flush({}, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();
    expect(expire).toHaveBeenCalled();
  });
});
