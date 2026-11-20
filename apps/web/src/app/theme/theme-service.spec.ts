import { TestBed } from '@angular/core/testing';
import { applyTheme, readThemePreference, ThemeService, themeStorageKey } from './theme-service';

describe('ThemeService', () => {
  const root = document.documentElement;

  beforeEach(() => {
    localStorage.clear();
    root.removeAttribute('data-theme');
  });

  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    root.removeAttribute('data-theme');
  });

  it('follows the system by default without setting an explicit theme', () => {
    const service = TestBed.inject(ThemeService);
    expect(service.preference()).toBe('system');
    expect(root.hasAttribute('data-theme')).toBe(false);
  });

  it('applies and persists only the chosen theme preference', () => {
    const service = TestBed.inject(ThemeService);
    service.set('dark');
    expect(root.getAttribute('data-theme')).toBe('dark');
    expect(localStorage.getItem(themeStorageKey)).toBe('dark');
    service.set('light');
    expect(root.getAttribute('data-theme')).toBe('light');
    service.set('system');
    expect(root.hasAttribute('data-theme')).toBe(false);
    expect(localStorage.getItem(themeStorageKey)).toBe('system');
    expect(localStorage.length).toBe(1);
  });

  it('restores a stored choice and ignores unknown stored values', () => {
    localStorage.setItem(themeStorageKey, 'dark');
    expect(readThemePreference()).toBe('dark');
    localStorage.setItem(themeStorageKey, '{"theme":"purple"}');
    expect(readThemePreference()).toBe('system');
    localStorage.setItem(themeStorageKey, 'dark');
    TestBed.inject(ThemeService);
    expect(root.getAttribute('data-theme')).toBe('dark');
  });

  it('still applies the theme when storage is blocked', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new DOMException('blocked'); });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new DOMException('blocked'); });
    expect(readThemePreference()).toBe('system');
    const service = TestBed.inject(ThemeService);
    expect(() => service.set('dark')).not.toThrow();
    expect(service.preference()).toBe('dark');
    expect(root.getAttribute('data-theme')).toBe('dark');
  });

  it('applyTheme sets and clears the document attribute', () => {
    applyTheme('light', root);
    expect(root.getAttribute('data-theme')).toBe('light');
    applyTheme('system', root);
    expect(root.hasAttribute('data-theme')).toBe(false);
  });
});
