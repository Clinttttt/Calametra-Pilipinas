import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';

import { BasemapStore, type BasemapId } from '../../../core/basemap/basemap-store';
import { Icon } from '../icon/icon';

/**
 * Base layer selector.
 *
 * ── On the interaction pattern ──────────────────────────────────────────────
 * A radio group in a popover, which is what hazard portals conventionally use for this and is
 * the correct semantic for an exclusive choice among named options. The pattern is kept
 * deliberately: it is well understood, keyboard-navigable by default, and inventing a novel
 * control for "pick one of four" would cost a reader familiarity for nothing.
 *
 * The presentation is this platform's own — hairline surfaces, tracked micro-labels, and each
 * option carrying the reason a reader would choose it rather than only its name. A base layer
 * here is an analytical decision, not a skin, so the control says what each one is for.
 */
@Component({
  selector: 'cal-basemap-switcher',
  standalone: true,
  imports: [Icon],
  templateUrl: './basemap-switcher.html',
  styleUrl: './basemap-switcher.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BasemapSwitcher {
  private readonly store = inject(BasemapStore);

  protected readonly options = this.store.options;
  protected readonly selectedId = this.store.selectedId;

  protected readonly expanded = signal(false);

  protected readonly selectedLabel = computed(() => this.store.selected().label);

  /** The active layer's licence note, if it has one. Surfaced, not buried. */
  protected readonly licenceCaveat = computed(() => this.store.selected().licenceCaveat);

  protected toggle(): void {
    this.expanded.update((open) => !open);
  }

  protected select(id: BasemapId): void {
    this.store.select(id);
    this.expanded.set(false);
  }

  /** Closes on Escape, which a popover must do to be usable from the keyboard. */
  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      this.expanded.set(false);
    }
  }
}
