import { DestroyRef } from '@angular/core';

export const filterDebounceMs = 350;

export interface FilterDebouncer {
  schedule(): void;
  now(): void;
  cancel(): void;
}

export function createFilterDebouncer(
  destroyRef: DestroyRef, action: () => void, delay = filterDebounceMs): FilterDebouncer {
  let timer: ReturnType<typeof setTimeout> | null = null;
  const cancel = (): void => {
    if (timer !== null) clearTimeout(timer);
    timer = null;
  };
  destroyRef.onDestroy(cancel);
  return {
    schedule: () => {
      cancel();
      timer = setTimeout(() => {
        timer = null;
        action();
      }, delay);
    },
    now: () => {
      cancel();
      action();
    },
    cancel,
  };
}
