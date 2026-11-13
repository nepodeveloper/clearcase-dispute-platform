import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { LoginPage } from './login-page';

describe('LoginPage', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [LoginPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

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

  for (const [role, destination] of [['Customer', '/'], ['Agent', '/agent/disputes'], ['Supervisor', '/agent/escalations']]) {
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
