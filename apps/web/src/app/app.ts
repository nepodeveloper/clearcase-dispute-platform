import { afterNextRender, Component, computed, ElementRef, HostListener, inject, Injector, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from './auth/auth-service';
import { homeUrl } from './auth/auth-guards';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './foundation.html',
})
export class App {
  private readonly injector = inject(Injector);
  private readonly router = inject(Router);
  protected readonly auth = inject(AuthService);
  protected readonly home = computed(() => {
    const user = this.auth.user();
    return user === null ? '/login' : homeUrl(user);
  });
  protected readonly customer = computed(() => this.auth.user()?.roles.includes('Customer') === true);
  private readonly mobileMenuToggle = viewChild<ElementRef<HTMLButtonElement>>('mobileMenuToggle');
  private readonly firstCustomerLink = viewChild<ElementRef<HTMLAnchorElement>>('firstCustomerLink');
  protected readonly title = signal('ClearCase');
  protected readonly mobileNavigationOpen = signal(false);

  constructor() {
    this.router.events.pipe(filter(event => event instanceof NavigationEnd), takeUntilDestroyed()).subscribe(() => {
      this.mobileNavigationOpen.set(false);
      afterNextRender(() => {
        if (!this.mobileNavigationOpen()) document.querySelector<HTMLElement>('main h1')?.focus();
      }, { injector: this.injector });
    });
  }

  protected async logout(): Promise<void> {
    if (await this.auth.logout()) await this.router.navigateByUrl('/login');
  }

  protected toggleMobileNavigation(): void {
    const shouldOpen = !this.mobileNavigationOpen();
    this.mobileNavigationOpen.set(shouldOpen);
    if (shouldOpen) {
      afterNextRender(() => {
        const focusFirstLink = () => {
          if (this.mobileNavigationOpen()) this.firstCustomerLink()?.nativeElement.focus();
        };
        if (typeof requestAnimationFrame === 'function')
          requestAnimationFrame(() => requestAnimationFrame(focusFirstLink));
        else focusFirstLink();
      }, { injector: this.injector });
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
