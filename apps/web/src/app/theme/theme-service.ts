import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';

export type ThemePreference = 'system' | 'light' | 'dark';

export const themePreferences: readonly ThemePreference[] = ['system', 'light', 'dark'];
export const themeStorageKey = 'clearcase.theme';

export function readThemePreference(): ThemePreference {
  try {
    const stored = globalThis.localStorage?.getItem(themeStorageKey);
    return themePreferences.find(preference => preference === stored) ?? 'system';
  } catch {
    return 'system';
  }
}

/** "system" removes the attribute so the prefers-color-scheme rules in the design tokens decide. */
export function applyTheme(preference: ThemePreference, root: HTMLElement = document.documentElement): void {
  if (preference === 'system') root.removeAttribute('data-theme');
  else root.setAttribute('data-theme', preference);
}

/** Applies the stored choice before the application renders, to avoid a flash of the wrong theme. */
export function applyStoredTheme(): void {
  applyTheme(readThemePreference());
}

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly root = inject(DOCUMENT).documentElement;
  private readonly current = signal<ThemePreference>(readThemePreference());
  readonly preference = this.current.asReadonly();

  constructor() {
    applyTheme(this.current(), this.root);
  }

  set(preference: ThemePreference): void {
    this.current.set(preference);
    applyTheme(preference, this.root);
    try {
      globalThis.localStorage?.setItem(themeStorageKey, preference);
    } catch {
      // The choice still applies for this visit when storage is blocked.
    }
  }
}
