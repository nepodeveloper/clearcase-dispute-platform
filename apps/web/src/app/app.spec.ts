import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { AuthService } from './auth/auth-service';

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
    expect(element.querySelector('nav[aria-label="Operations navigation"] a[href="/agent/disputes"]')).not.toBeNull();
  });
});
