import { Injectable, signal } from '@angular/core';

export type NotificationLevel = 'success' | 'error' | 'info';

export interface AppNotification {
  id: number;
  level: NotificationLevel;
  message: string;
}

/**
 * 轻量全局通知服务（应用级单例）。
 * 以 signal 暴露当前通知列表，供布局层的通知组件订阅渲染。
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private seq = 0;
  private readonly _items = signal<AppNotification[]>([]);
  readonly items = this._items.asReadonly();

  success(message: string): void {
    this.push('success', message);
  }

  error(message: string): void {
    this.push('error', message);
  }

  info(message: string): void {
    this.push('info', message);
  }

  dismiss(id: number): void {
    this._items.update((list) => list.filter((n) => n.id !== id));
  }

  private push(level: NotificationLevel, message: string): void {
    const item: AppNotification = { id: ++this.seq, level, message };
    this._items.update((list) => [...list, item]);
    setTimeout(() => this.dismiss(item.id), 4000);
  }
}
