import { DOCUMENT } from '@angular/common';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface AppNotification {
  id: string;
  disputeId: string;
  disputeReference: string;
  eventType: string;
  message: string;
  occurredAt: string;
  isRead: boolean;
}

export interface NotificationFeed {
  unreadCount: number;
  items: AppNotification[];
}

export type FeedState = 'idle' | 'loading' | 'loaded' | 'error';

export const notificationPollMs = 60_000;
const feedLimit = 50;

@Injectable({ providedIn: 'root' })
export class NotificationsService {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);
  private readonly count = signal(0);
  private readonly feed = signal<AppNotification[]>([]);
  private readonly feedStatus = signal<FeedState>('idle');
  private readonly marking = signal(false);
  private readonly markFailed = signal(false);
  private timer: ReturnType<typeof setInterval> | null = null;
  private readonly onVisibility = () => {
    if (this.document.visibilityState === 'visible') void this.pollUnread();
  };
  readonly unreadCount = this.count.asReadonly();
  readonly items = this.feed.asReadonly();
  readonly state = this.feedStatus.asReadonly();
  readonly markingRead = this.marking.asReadonly();
  readonly markError = this.markFailed.asReadonly();

  start(): void {
    if (this.timer !== null) return;
    void this.pollUnread();
    this.timer = setInterval(() => {
      if (this.document.visibilityState === 'visible') void this.pollUnread();
    }, notificationPollMs);
    this.document.addEventListener('visibilitychange', this.onVisibility);
  }

  stop(): void {
    if (this.timer !== null) clearInterval(this.timer);
    this.timer = null;
    this.document.removeEventListener('visibilitychange', this.onVisibility);
    this.count.set(0);
    this.feed.set([]);
    this.feedStatus.set('idle');
    this.markFailed.set(false);
  }

  async pollUnread(): Promise<void> {
    try {
      const result = await firstValueFrom(this.http.get<NotificationFeed>('/api/v1/notifications',
        { params: new HttpParams().set('limit', 1) }));
      this.count.set(result.unreadCount);
    } catch {
      // A failed background check keeps the last known count; the notifications page offers an explicit retry.
    }
  }

  async load(): Promise<void> {
    this.feedStatus.set('loading');
    this.markFailed.set(false);
    try {
      const result = await firstValueFrom(this.http.get<NotificationFeed>('/api/v1/notifications',
        { params: new HttpParams().set('limit', feedLimit) }));
      this.feed.set(result.items);
      this.count.set(result.unreadCount);
      this.feedStatus.set('loaded');
    } catch {
      this.feedStatus.set('error');
    }
  }

  async markAllRead(): Promise<boolean> {
    if (this.marking()) return false;
    this.marking.set(true);
    this.markFailed.set(false);
    try {
      await firstValueFrom(this.http.post<void>('/api/v1/notifications/read', {}));
      this.feed.update(items => items.map(item => ({ ...item, isRead: true })));
      this.count.set(0);
      return true;
    } catch {
      this.markFailed.set(true);
      return false;
    } finally {
      this.marking.set(false);
    }
  }
}
