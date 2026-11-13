import { afterNextRender, Component, ElementRef, HostListener, inject, Injector, signal, viewChild } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './foundation.html',
})
export class App {
  private readonly injector = inject(Injector);
  private readonly mobileMenuToggle = viewChild<ElementRef<HTMLButtonElement>>('mobileMenuToggle');
  private readonly firstCustomerLink = viewChild<ElementRef<HTMLAnchorElement>>('firstCustomerLink');
  protected readonly title = signal('ClearCase');
  protected readonly mobileNavigationOpen = signal(false);

  protected toggleMobileNavigation(): void {
    const shouldOpen = !this.mobileNavigationOpen();
    this.mobileNavigationOpen.set(shouldOpen);
    if (shouldOpen) {
      afterNextRender(() => this.firstCustomerLink()?.nativeElement.focus(), { injector: this.injector });
    } else {
      this.mobileMenuToggle()?.nativeElement.focus();
    }
  }

  protected closeMobileNavigation(): void {
    const wasOpen = this.mobileNavigationOpen();
    this.mobileNavigationOpen.set(false);
    if (wasOpen) this.mobileMenuToggle()?.nativeElement.focus();
  }

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    this.closeMobileNavigation();
  }
}
