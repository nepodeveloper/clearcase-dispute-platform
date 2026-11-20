/** True when a single-key shortcut may run: no modifier is held and focus is not in an editable control. */
export function shortcutsActive(event: KeyboardEvent): boolean {
  if (event.defaultPrevented || event.isComposing || event.ctrlKey || event.metaKey || event.altKey || event.shiftKey) return false;
  const target = event.target;
  if (!(target instanceof HTMLElement)) return true;
  return !target.closest('input, textarea, select, [contenteditable]:not([contenteditable="false"])') && !target.isContentEditable;
}

/** Enter keeps its native meaning on links, buttons and disclosure summaries. */
export function hasNativeEnterAction(target: EventTarget | null): boolean {
  return target instanceof HTMLElement && target.closest('a[href], button, summary') !== null;
}
