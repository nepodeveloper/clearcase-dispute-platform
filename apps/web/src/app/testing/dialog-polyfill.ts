/** jsdom does not implement HTMLDialogElement.showModal/close; this gives tests the same open/close contract. */
export function installDialogPolyfill(): void {
  const prototype = HTMLDialogElement.prototype;
  if (typeof prototype.showModal === 'function') return;
  prototype.showModal = function showModal(this: HTMLDialogElement): void {
    this.setAttribute('open', '');
  };
  prototype.close = function close(this: HTMLDialogElement): void {
    if (!this.hasAttribute('open')) return;
    this.removeAttribute('open');
    this.dispatchEvent(new Event('close'));
  };
}
