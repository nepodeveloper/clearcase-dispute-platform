import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { DemoApp } from './demo-app';
import { DemoSession } from './demo-session';

describe('UI demo navigation', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [DemoApp], providers: [provideRouter([]), DemoSession] });
  });

  it('shows only the selected role destinations and traps and restores mobile drawer focus', async () => {
    const fixture = TestBed.createComponent(DemoApp);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('a[href="/agent/disputes"]')).toBeNull();
    TestBed.inject(DemoSession).enter('agent');
    fixture.detectChanges();
    expect(root.querySelector('a[href="/agent/disputes"]')).not.toBeNull();
    expect(root.querySelector('a[href="/disputes"]')).toBeNull();
    const toggle = root.querySelector<HTMLButtonElement>('.mobile-menu-toggle')!;
    toggle.focus();
    toggle.click();
    fixture.detectChanges();
    await fixture.whenStable();
    const sidebar = root.querySelector<HTMLElement>('.sidebar')!;
    const controls = sidebar.querySelectorAll<HTMLElement>('a[href], button');
    expect(document.activeElement).toBe(controls[0]);
    expect(sidebar.getAttribute('aria-modal')).toBe('true');
    expect(root.querySelector<HTMLElement>('.main-column')?.inert).toBe(true);
    controls[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(controls[controls.length - 1]);
    controls[controls.length - 1].dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(root.querySelector<HTMLElement>('.main-column')?.inert).toBe(false);
    expect(document.activeElement).toBe(toggle);
  });
});
