import { DOCUMENT } from '@angular/common';
import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { BACKGROUND_CHECK } from '../auth/session-interceptor';

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
  private generation = 0;
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
    this.generation += 1;
    if (this.timer !== null) clearInterval(this.timer);
    this.timer = null;
    this.document.removeEventListener('visibilitychange', this.onVisibility);
    this.count.set(0);
    this.feed.set([]);
    this.feedStatus.set('idle');
    this.marking.set(false);
    this.markFailed.set(false);
  }

  async pollUnread(): Promise<void> {
    const generation = this.generation;
    try {
      const result = await firstValueFrom(this.http.get<NotificationFeed>('/api/v1/notifications',
        { params: new HttpParams().set('limit', 1), context: new HttpContext().set(BACKGROUND_CHECK, true) }));
      if (generation === this.generation) this.count.set(result.unreadCount);
    } catch {
      // A failed background check keeps the last known count; the notifications page offers an explicit retry.
    }
  }

  async load(): Promise<void> {
    const generation = this.generation;
    this.feedStatus.set('loading');
    this.markFailed.set(false);
    try {
      const result = await firstValueFrom(this.http.get<NotificationFeed>('/api/v1/notifications',
        { params: new HttpParams().set('limit', feedLimit) }));
      if (generation !== this.generation) return;
      this.feed.set(result.items);
      this.count.set(result.unreadCount);
      this.feedStatus.set('loaded');
    } catch {
      if (generation === this.generation) this.feedStatus.set('error');
    }
  }

  async markAllRead(): Promise<boolean> {
    if (this.marking()) return false;
    const generation = this.generation;
    this.marking.set(true);
    this.markFailed.set(false);
    try {
      await firstValueFrom(this.http.post<void>('/api/v1/notifications/read', {}));
      if (generation !== this.generation) return false;
      this.feed.update(items => items.map(item => ({ ...item, isRead: true })));
      this.count.set(0);
      return true;
    } catch {
      if (generation === this.generation) this.markFailed.set(true);
      return false;
    } finally {
      if (generation === this.generation) this.marking.set(false);
    }
  }
}
