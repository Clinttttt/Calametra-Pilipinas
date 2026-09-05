import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { Icon } from './shared/ui/icon/icon';
import { NotificationStore } from './core/notifications/notification-store';
import { PresentationStore } from './core/presentation/presentation-store';
import { type IconName } from './shared/ui/icon/icon-paths';

interface NavigationItem {
  readonly path: string;
  readonly label: string;
  readonly icon: IconName;
  readonly available: boolean;
}

/**
 * Application shell.
 *
 * Owns only the persistent chrome: the brand bar, the navigation rail and the
 * notification region. Tools belong to the map's own right-hand rail rather than
 * here, so the shell stays a frame and never competes with the content.
 */
@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Icon],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly notificationStore = inject(NotificationStore);
  private readonly presentationStore = inject(PresentationStore);

  protected readonly notifications = this.notificationStore.notifications;

  /**
   * Whether the chrome is collapsed for a full-bleed view.
   *
   * Held in a store rather than passed down, because the routed feature is what
   * knows a full-bleed view makes sense — the shell only needs to get out of the
   * way when asked.
   */
  protected readonly presenting = this.presentationStore.presenting;

  /**
   * Navigation destinations.
   *
   * Items for unbuilt phases are shown and disabled rather than hidden. A staged
   * platform benefits from showing its intended shape, and nothing pretends to work.
   */
  protected readonly navigation: readonly NavigationItem[] = [
    { path: '/explore', label: 'Explore', icon: 'explore', available: true },
    { path: '/time', label: 'Time', icon: 'time', available: false },
    { path: '/events', label: 'Events', icon: 'events', available: false },
    { path: '/stories', label: 'Stories', icon: 'stories', available: false },
    { path: '/compare', label: 'Compare', icon: 'compare', available: false },
    { path: '/about-data', label: 'Sources', icon: 'data-sources', available: true },
  ];

  protected dismiss(id: number): void {
    this.notificationStore.dismiss(id);
  }
}
