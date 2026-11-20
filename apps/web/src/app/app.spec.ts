import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { AuthService } from './auth/auth-service';
import { sessionInterceptor } from './auth/session-interceptor';
import { sessionClock, SessionTimeoutService, sessionLifetimeMs, sessionWarningMs } from './auth/session-timeout-service';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    const restore = TestBed.inject(AuthService).restore();
    TestBed.inject(HttpTestingController).expectOne('/api/v1/auth/me').flush({
      username: 'customer.demo', actorType: 'customer', roles: ['Customer'], csrfToken: 'csrf',
    });
    await restore;
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('renders the ClearCase identity, customer navigation and a single scope note', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('.brand-name strong')?.textContent?.trim()).toBe('ClearCase');
    expect(compiled.querySelector('nav[aria-label="Customer navigation"] a[href="/"]')?.textContent)
      .toContain('Transactions');
    expect(compiled.querySelector('nav[aria-label="Customer navigation"] a[href="/disputes"]')?.textContent)
      .toContain('My disputes');
    expect(compiled.textContent).not.toContain('Unofficial demo');
    expect(compiled.querySelector('select')).toBeNull();
    const scopeNote = compiled.querySelector('[data-testid="scope-note"]')?.textContent;
    expect(scopeNote).toContain('Synthetic data and local assessment accounts only');
    expect(scopeNote).toContain('Not an official Capitec interface');
  });

  it('exposes and dismisses the mobile navigation with Escape', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const toggle = compiled.querySelector<HTMLButtonElement>('.mobile-menu-toggle')!;
    const firstLink = compiled.querySelector<HTMLAnchorElement>('#primary-navigation a')!;
    expect(toggle.getAttribute('aria-expanded')).toBe('false');

    toggle.focus();
    toggle.click();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(compiled.querySelector('.sidebar')?.classList.contains('is-open')).toBe(true);
    expect(document.activeElement).toBe(firstLink);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    await fixture.whenStable();
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(compiled.querySelector('.sidebar')?.classList.contains('is-open')).toBe(false);
    expect(document.activeElement).toBe(toggle);
  });

  it('traps focus in the open drawer and makes the page behind it inert', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;

    compiled.querySelector<HTMLButtonElement>('.mobile-menu-toggle')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    const sidebar = compiled.querySelector<HTMLElement>('.sidebar')!;
    const controls = sidebar.querySelectorAll<HTMLElement>('a[href], button');
    const last = controls[controls.length - 1];
    expect(sidebar.getAttribute('aria-modal')).toBe('true');
    expect(compiled.querySelector<HTMLElement>('.main-column')?.inert).toBe(true);

    controls[0].focus();
    controls[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(last);
    last.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(controls[0]);
  });

  it('shows a non-modal status banner near session expiry and refreshes on demand', async () => {
    let now = 5_000_000;
    TestBed.resetTestingModule();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(withInterceptors([sessionInterceptor])), provideHttpClientTesting(), provideRouter([]),
        { provide: sessionClock, useValue: () => now }],
    }).compileComponents();
    const http = TestBed.inject(HttpTestingController);
    const restore = TestBed.inject(AuthService).restore();
    http.expectOne('/api/v1/auth/me').flush({
      username: 'customer.demo', actorType: 'customer', roles: ['Customer'], csrfToken: 'csrf',
    });
    await restore;
    const timeout = TestBed.inject(SessionTimeoutService);
    timeout.recordActivity();
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('.session-banner')).toBeNull();

    now += sessionLifetimeMs - sessionWarningMs;
    timeout.tick();
    fixture.detectChanges();
    const banner = element.querySelector('.session-banner')!;
    expect(banner.getAttribute('role')).toBe('status');
    expect(banner.textContent).toContain('5 minutes');

    const stay = Array.from(banner.querySelectorAll('button')).find(b => b.textContent?.trim() === 'Stay signed in')!;
    stay.click();
    http.expectOne('/api/v1/auth/me').flush({
      username: 'customer.demo', actorType: 'customer', roles: ['Customer'], csrfToken: 'csrf-2',
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.querySelector('.session-banner')).toBeNull();
  });

  it('renders decorative SVG navigation icons', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const icons = (fixture.nativeElement as HTMLElement).querySelectorAll('#primary-navigation svg.nav-icon');
    expect(icons.length).toBe(3);
    icons.forEach(icon => expect(icon.getAttribute('aria-hidden')).toBe('true'));
  });

  it('adds a notifications link and a bell whose accessible name carries the unread count', async () => {
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('nav[aria-label="Customer navigation"] a[href="/notifications"]')?.textContent)
      .toContain('Notifications');
    const bell = element.querySelector<HTMLAnchorElement>('[data-testid="notification-bell"]')!;
    expect(bell.getAttribute('href')).toBe('/notifications');
    expect(bell.getAttribute('aria-label')).toBe('Notifications');
    expect(element.querySelector('[data-testid="notification-badge"]')).toBeNull();

    const poll = http.expectOne(request => request.url === '/api/v1/notifications');
    expect(poll.request.params.get('limit')).toBe('1');
    poll.flush({ unreadCount: 3, items: [] });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(bell.getAttribute('aria-label')).toBe('Notifications, 3 unread');
    const badge = element.querySelector('[data-testid="notification-badge"]')!;
    expect(badge.textContent?.trim()).toBe('3');
    expect(badge.getAttribute('aria-hidden')).toBe('true');
  });

  it('lets the user choose System, Light or Dark and remembers only that choice', async () => {
    localStorage.clear();
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const group = element.querySelector('[role="group"][aria-label="Colour theme"]')!;
    const buttons = Array.from(group.querySelectorAll<HTMLButtonElement>('button'));
    expect(buttons.map(button => button.textContent?.trim())).toEqual(['System', 'Light', 'Dark']);
    expect(buttons.map(button => button.getAttribute('aria-pressed'))).toEqual(['true', 'false', 'false']);

    buttons[2].click();
    fixture.detectChanges();
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(buttons.map(button => button.getAttribute('aria-pressed'))).toEqual(['false', 'false', 'true']);
    expect(localStorage.getItem('clearcase.theme')).toBe('dark');
    expect(localStorage.length).toBe(1);

    buttons[0].click();
    fixture.detectChanges();
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
    document.documentElement.removeAttribute('data-theme');
    localStorage.clear();
  });

  it('shows only operations navigation for an authenticated employee', async () => {
    const login = TestBed.inject(AuthService).login('agent.demo', 'temporary-input');
    TestBed.inject(HttpTestingController).expectOne('/api/v1/auth/login').flush({
      username: 'agent.demo', actorType: 'bankEmployee', roles: ['Agent'], csrfToken: 'agent-csrf',
    });
    await login;
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('nav[aria-label="Customer navigation"]')).toBeNull();
    expect(element.querySelector('a[href="/disputes"]')).toBeNull();
    expect(element.querySelector('[data-testid="notification-bell"]')).toBeNull();
    expect(element.querySelector('a[href="/notifications"]')).toBeNull();
    TestBed.inject(HttpTestingController).expectNone(request => request.url === '/api/v1/notifications');
    expect(element.querySelector('nav[aria-label="Operations navigation"] a[href="/agent/disputes"]')).not.toBeNull();
  });
});
