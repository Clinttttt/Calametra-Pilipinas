import { Injectable, computed, signal } from '@angular/core';

/**
 * Whether the interface is collapsed to a full-bleed presentation view.
 *
 * Shared state rather than a component input because two parties need it and
 * neither owns the other: the routed feature decides *when* a full-bleed view is
 * appropriate and offers the control, while the shell is what has to collapse.
 * Passing it up through outputs would make the shell depend on which feature is
 * routed.
 *
 * Named "presentation" rather than "fullscreen" deliberately — this is not the
 * browser Fullscreen API. It hides application chrome while leaving the browser
 * as it is, which is what is wanted when projecting during a defence and still
 * needing the address bar and tabs.
 */
@Injectable({ providedIn: 'root' })
export class PresentationStore {
  private readonly state = signal(false);

  readonly presenting = computed(() => this.state());

  toggle(): void {
    this.state.update((presenting) => !presenting);
  }

  exit(): void {
    this.state.set(false);
  }
}
