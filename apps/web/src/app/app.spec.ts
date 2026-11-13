import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
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
      'Real customer authentication is not available',
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
});
