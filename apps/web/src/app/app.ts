import { afterNextRender, Component, computed, ElementRef, HostListener, inject, Injector, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService, AuthUser } from './auth/auth-service';
import { homeUrl } from './auth/auth-guards';

interface NavigationLink { label: string; url: string; icon: string; section: string }

function navigationLinks(user: AuthUser): NavigationLink[] {
  const links: NavigationLink[] = [];
  if (user.roles.includes('Customer')) links.push(
    { label: 'Transactions', url: '/', icon: '\u25a4', section: 'transactions' },
    { label: 'My disputes', url: '/disputes', icon: '\u25a6', section: 'disputes' });
  if (user.roles.includes('Agent'))
    links.push({ label: 'Agent work queue', url: '/agent/disputes', icon: '\u2637', section: 'agent' });
  if (user.roles.includes('Supervisor'))
    links.push({ label: 'Supervisor queue', url: '/agent/escalations', icon: '\u25c8', section: 'supervisor' });
  return links;
}

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterOutlet],
  templateUrl: './app.html',
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
  protected readonly links = computed(() => {
    const user = this.auth.user();
    return user === null ? [] : navigationLinks(user);
  });
  protected readonly initials = computed(() => this.auth.user()?.username.slice(0, 2).toUpperCase() ?? '');
  protected readonly menuOpen = signal(false);
  protected readonly label = signal('ClearCase');
  protected readonly section = signal('');
  private readonly sidebar = viewChild<ElementRef<HTMLElement>>('sidebar');
  private readonly menuButton = viewChild<ElementRef<HTMLButtonElement>>('menuButton');
  private readonly main = viewChild<ElementRef<HTMLElement>>('main');

  constructor() {
    this.router.events.pipe(filter(event => event instanceof NavigationEnd), takeUntilDestroyed()).subscribe(() => {
      let route = this.router.routerState.snapshot.root;
      while (route.firstChild !== null) route = route.firstChild;
      this.label.set(typeof route.data['label'] === 'string' ? route.data['label'] : 'ClearCase');
      this.section.set(typeof route.data['section'] === 'string' ? route.data['section'] : '');
      this.menuOpen.set(false);
      afterNextRender(() => {
        if (!this.menuOpen()) this.main()?.nativeElement.querySelector<HTMLElement>('h1')?.focus();
      }, { injector: this.injector });
    });
  }

  protected async logout(): Promise<void> {
    if (await this.auth.logout()) await this.router.navigateByUrl('/login');
  }

  protected openMenu(): void {
    this.menuOpen.set(true);
    afterNextRender(() => {
      const sidebar = this.sidebar()?.nativeElement;
      if (this.menuOpen() && sidebar !== undefined && !sidebar.contains(document.activeElement))
        sidebar.querySelector<HTMLElement>('nav a')?.focus();
    }, { injector: this.injector });
  }

  protected closeMenu(): void {
    if (!this.menuOpen()) return;
    this.menuOpen.set(false);
    // The header is inert while the drawer is open, so restore focus once it renders closed.
    afterNextRender(() => this.menuButton()?.nativeElement.focus(), { injector: this.injector });
  }

  protected menuKeydown(event: KeyboardEvent): void {
    if (!this.menuOpen() || event.key !== 'Tab') return;
    const controls = this.sidebar()?.nativeElement.querySelectorAll<HTMLElement>('a[href], button');
    if (controls === undefined || controls.length === 0) return;
    const first = controls[0];
    const last = controls[controls.length - 1];
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
  }

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    this.closeMenu();
  }

  @HostListener('window:resize')
  protected resized(): void {
    if (this.menuOpen() && window.matchMedia('(min-width: 851px)').matches) this.menuOpen.set(false);
  }
}
