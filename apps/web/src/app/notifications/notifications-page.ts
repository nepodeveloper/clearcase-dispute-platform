import { DatePipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PageHeading } from '../ui/ui-components';
import { NotificationsService } from './notifications-service';

@Component({
  selector: 'app-notifications-page',
  imports: [DatePipe, RouterLink, PageHeading],
  template: `
    <cc-page-heading title="Notifications" eyebrow="Customer portal"
      description="Updates from your dispute timelines. These appear here only; nothing is sent by email or SMS." />
    <section class="panel" aria-labelledby="notifications-heading" [attr.aria-busy]="service.state() === 'loading'">
      <div class="panel-title">
        <h2 id="notifications-heading">Recent updates</h2>
        <button class="cc-button cc-button--secondary" type="button" data-testid="mark-all-read"
          [disabled]="service.unreadCount() === 0 || service.markingRead()" (click)="markAllRead()">
          {{ service.markingRead() ? 'Marking...' : 'Mark all as read' }}</button>
      </div>
      <p class="muted" role="status" data-testid="notification-status">{{ status() }}</p>
      @if (service.markError()) {
        <p class="cc-form-error" role="alert">Unable to mark notifications as read. Please try again.</p>
      }
      @if (service.state() === 'error') {
        <div class="state-message" role="alert"><p>Unable to load notifications. Please try again.</p>
          <button class="cc-button cc-button--secondary" type="button" data-testid="retry" (click)="load()">Retry</button></div>
      } @else if (service.state() === 'loaded' && service.items().length === 0) {
        <p class="cc-empty">No notifications yet. Updates to your disputes will appear here.</p>
      } @else if (service.items().length > 0) {
        <ul class="notification-list" aria-label="Notifications, newest first">
          @for (item of service.items(); track item.id) {
            <li class="notification" [class.is-unread]="!item.isRead">
              <div class="notification-head">
                @if (!item.isRead) { <span class="tag" data-testid="unread-tag">New<span class="cc-sr-only"> notification</span></span> }
                <a [routerLink]="['/disputes', item.disputeId]"
                  [attr.aria-label]="'View dispute ' + item.disputeReference">{{ item.disputeReference }}</a>
                <time [attr.datetime]="item.occurredAt">{{ item.occurredAt | date:'dd MMM yyyy, HH:mm' }}</time>
              </div>
              <p>{{ item.message }}</p>
            </li>
          }
        </ul>
      }
    </section>`,
})
export class NotificationsPage implements OnInit {
  protected readonly service = inject(NotificationsService);
  private readonly confirmation = signal('');
  protected readonly status = computed(() => {
    if (this.confirmation() !== '' && this.service.unreadCount() === 0) return this.confirmation();
    switch (this.service.state()) {
      case 'loading': return 'Loading notifications...';
      case 'loaded': {
        const unread = this.service.unreadCount();
        return unread === 0 ? 'You are all caught up.' : `${unread} unread ${unread === 1 ? 'notification' : 'notifications'}.`;
      }
      default: return '';
    }
  });

  ngOnInit(): void { void this.load(); }

  protected async load(): Promise<void> {
    this.confirmation.set('');
    await this.service.load();
  }

  protected async markAllRead(): Promise<void> {
    this.confirmation.set('');
    if (await this.service.markAllRead()) this.confirmation.set('All notifications marked as read.');
  }
}
