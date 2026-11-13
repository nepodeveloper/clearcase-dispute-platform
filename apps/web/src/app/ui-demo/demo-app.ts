import { afterNextRender, Component, computed, ElementRef, HostListener, inject, Injector, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { DEMO_IDS } from './demo-data';
import { DEMO_HOME, DemoSession } from './demo-session';

interface ScreenLink { label: string; url: string; icon: string; section: string }

@Component({
  selector: 'cc-demo-root',
  imports: [RouterLink, RouterOutlet],
  templateUrl: './demo-app.html',
})
export class DemoApp {
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  protected readonly session = inject(DemoSession);
  protected readonly menuOpen = signal(false);
  protected readonly label = signal('Login');
  protected readonly section = signal('login');
  private readonly sidebar = viewChild<ElementRef<HTMLElement>>('sidebar');
  private readonly menuButton = viewChild<ElementRef<HTMLButtonElement>>('menuButton');
  private readonly main = viewChild<ElementRef<HTMLElement>>('main');
  protected readonly initials = computed(() => ({ customer: 'TN', agent: 'AG', supervisor: 'SP' })[this.session.role() ?? 'customer']);
  protected readonly links = computed<ScreenLink[]>(() => {
    const login = { label: 'Login / choose journey', url: '/login', icon: '\u2302', section: 'login' };
    switch (this.session.role()) {
      case 'agent': return [login,
        { label: 'Agent work queue', url: '/agent/disputes', icon: '\u2637', section: 'agent-queue' },
        { label: 'Case investigation', url: `/agent/disputes/${DEMO_IDS.investigation}`, icon: '\u2315', section: 'investigation' },
        { label: 'Escalate case', url: `/agent/disputes/${DEMO_IDS.investigation}/escalate`, icon: '\u2197', section: 'escalate' },
      ];
      case 'supervisor': return [login,
        { label: 'Supervisor queue', url: '/agent/escalations', icon: '\u25c8', section: 'supervisor' },
        { label: 'Supervisor review', url: `/agent/escalations/${DEMO_IDS.escalation}`, icon: '\u2713', section: 'supervisor-detail' },
      ];
      default: return [login,
        { label: 'Transactions', url: '/', icon: '\u25a4', section: 'transactions' },
        { label: 'Submit a dispute', url: `/transactions/${DEMO_IDS.availableTransaction}`, icon: '\u25a3', section: 'transaction' },
        { label: 'My disputes', url: '/disputes', icon: '\u25a6', section: 'disputes' },
        { label: 'Dispute timeline', url: `/disputes/${DEMO_IDS.timeline}`, icon: '\u25f7', section: 'timeline' },
      ];
    }
  });

  constructor() {
    this.router.events.pipe(filter(event => event instanceof NavigationEnd), takeUntilDestroyed()).subscribe(() => {
      let route = this.router.routerState.snapshot.root;
      while (route.firstChild !== null) route = route.firstChild;
      this.label.set(typeof route.data['label'] === 'string' ? route.data['label'] : 'ClearCase');
      this.section.set(typeof route.data['section'] === 'string' ? route.data['section'] : '');
      this.menuOpen.set(false);
      afterNextRender(() => {
        if (this.menuOpen()) return;
        const main = this.main()?.nativeElement;
        (main?.querySelector<HTMLElement>('h1') ?? main)?.focus();
        window.scrollTo({ top: 0, behavior: 'instant' });
      }, { injector: this.injector });
    });
  }

  protected selectJourney(value: string): void {
    if (value === 'customer' || value === 'agent' || value === 'supervisor') {
      this.session.enter(value);
      void this.router.navigateByUrl(DEMO_HOME[value]);
    } else {
      this.session.leave();
      void this.router.navigateByUrl('/login');
    }
  }

  protected openMenu(): void {
    this.menuOpen.set(true);
    afterNextRender(() => {
      const sidebar = this.sidebar()?.nativeElement;
      if (this.menuOpen() && sidebar !== undefined && !sidebar.contains(document.activeElement)) {
        sidebar.querySelector<HTMLElement>('button')?.focus();
      }
    }, { injector: this.injector });
  }

  protected closeMenu(): void {
    this.menuOpen.set(false);
    afterNextRender(() => this.menuButton()?.nativeElement.focus(), { injector: this.injector });
  }

  protected menuKeydown(event: KeyboardEvent): void {
    if (!this.menuOpen()) return;
    if (event.key === 'Escape') { event.preventDefault(); this.closeMenu(); return; }
    if (event.key !== 'Tab') return;
    const controls = this.sidebar()?.nativeElement.querySelectorAll<HTMLElement>('a[href], button');
    if (controls === undefined || controls.length === 0) return;
    const first = controls[0];
    const last = controls[controls.length - 1];
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
  }

  @HostListener('window:resize')
  protected resized(): void {
    if (this.menuOpen() && window.matchMedia('(min-width: 851px)').matches) {
      this.menuOpen.set(false);
      this.main()?.nativeElement.focus();
    }
  }
}
