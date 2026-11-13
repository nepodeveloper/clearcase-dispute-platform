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

  it('should render the ClearCase identity, customer navigation and demo scope', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('.brand-name strong')?.textContent?.trim()).toBe('ClearCase');
    expect(compiled.querySelector('nav[aria-label="Customer navigation"] a[href="/"]')?.textContent)
      .toContain('Transactions');
    expect(compiled.querySelector('nav[aria-label="Customer navigation"] a[href="/disputes"]')?.textContent)
      .toContain('Your disputes');
    expect(compiled.textContent).toContain('Unofficial demo');
    expect(compiled.querySelector('[data-testid="scope-note"]')?.textContent).toContain(
      'Synthetic data and local assessment accounts only',
    );
  });

  it('exposes and dismisses the mobile navigation with Escape', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const toggle = compiled.querySelector<HTMLButtonElement>('.mobile-menu-toggle')!;
    const firstLink = compiled.querySelector<HTMLAnchorElement>('#customer-navigation a')!;
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
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(compiled.querySelector('.sidebar')?.classList.contains('is-open')).toBe(false);
    expect(document.activeElement).toBe(toggle);
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
