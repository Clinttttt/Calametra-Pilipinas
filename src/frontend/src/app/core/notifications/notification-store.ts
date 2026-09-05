import { Injectable, computed, signal } from '@angular/core';

export interface Notification {
  readonly id: number;
  readonly kind: 'error' | 'info';
  readonly message: string;
}

/**
 * Transient user-facing messages.
 *
 * Signal-based rather than an RxJS subject: notifications are state that the
 * view reads, not a stream it subscribes to, and signals remove the need for
 * subscription lifecycle handling in the component that renders them.
 *
 * Duplicate suppression is deliberate. A map pan can fire several requests at
 * once, and if the API is unreachable every one of them fails — without
 * de-duplication the user gets a stack of identical toasts describing a single
 * problem.
 */
@Injectable({ providedIn: 'root' })
export class NotificationStore {
  private static readonly dismissAfterMs = 6_000;

  private nextId = 1;
  private readonly items = signal<readonly Notification[]>([]);

  readonly notifications = computed(() => this.items());

  error(message: string): void {
    this.push('error', message);
  }

  info(message: string): void {
    this.push('info', message);
  }

  dismiss(id: number): void {
    this.items.update((current) => current.filter((item) => item.id !== id));
  }

  private push(kind: Notification['kind'], message: string): void {
    if (this.items().some((item) => item.message === message)) {
      return;
    }

    const id = this.nextId++;

    this.items.update((current) => [...current, { id, kind, message }]);

    setTimeout(() => this.dismiss(id), NotificationStore.dismissAfterMs);
  }
}
