import { ChangeDetectionStrategy, Component, inject, output } from '@angular/core';

import { HazardModeStore, type HazardMode } from '../../../core/hazards/hazard-mode-store';
import { Icon } from '../../../shared/ui/icon/icon';

/**
 * Which hazard the map is showing.
 *
 * ── Why nothing is shown until this is used ─────────────────────────────────
 * The platform used to open with the whole earthquake catalogue plotted: 27,241 events drawn several
 * symbols deep, conveying neither location nor depth, and presenting one hazard as the subject of a
 * multi-hazard platform. The map now opens empty and the reader chooses.
 *
 * ── Why a rail panel and not a modal ────────────────────────────────────────
 * This began as a centred card over a scrim. That was wrong twice: it blocked the map behind a
 * dialogue nobody had asked for, and it broke the platform's own convention that tools are quiet
 * icons on the right rail which open panels. Every other tool already follows that rule, so a modal
 * made hazard selection the one thing that behaved differently — and it read as clutter dropped over
 * the map rather than as part of the interface.
 *
 * The component owns no state. It renders the catalogue, reports which entry is active so the panel
 * can mark it, and emits the reader's choice; the map component decides what that choice means.
 */
@Component({
  selector: 'cal-hazard-chooser',
  imports: [Icon],
  templateUrl: './hazard-chooser.html',
  styleUrl: './hazard-chooser.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HazardChooser {
  private readonly store = inject(HazardModeStore);

  /** The hazard the reader picked. The parent owns what happens next. */
  readonly chosen = output<HazardMode>();

  /** Dismisses the panel without changing the selection. */
  readonly closed = output<void>();

  protected readonly options = HazardModeStore.options;

  /** Read from the store rather than passed in: the panel only ever mirrors the real selection. */
  protected readonly selected = this.store.selected;
}
