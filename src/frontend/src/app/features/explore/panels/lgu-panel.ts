import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { Icon } from '../../../shared/ui/icon/icon';
import { type IconName } from '../../../shared/ui/icon/icon-paths';
import { LguSelectionStore } from '../../../core/administrative/lgu-selection-store';

/**
 * What the reader selected, and what that selection does and does not mean.
 *
 * **Deliberately not the place panel.** ADR-005 D1 keeps containment and proximity apart: this names the
 * administrative unit a point falls in, while the place panel answers how much of the archive lies within
 * a distance of one representative point. Selecting a municipality here does not open a radius, and the
 * panel says so — otherwise a reader would reasonably assume the two are the same question asked twice.
 *
 * The area is shown because ADR-005 D1 requires any figure derived from a boundary to state the unit's
 * area beside it. Philippine LGUs differ in area by more than two orders of magnitude, so a count inside a
 * boundary encodes land area as much as anything else, and the area is what lets a reader see that.
 *
 * It carries its own surface, as `place-panel` does. The first version relied on variables that do not
 * exist in this project's token set, so `color-mix` resolved to nothing and the panel rendered as text
 * floating on the satellite imagery — legible in a screenshot and not on a coastline.
 */
@Component({
  selector: 'cal-lgu-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe, Icon],
  templateUrl: './lgu-panel.html',
  styleUrl: './lgu-panel.scss',
})
export class LguPanel {
  protected readonly store = inject(LguSelectionStore);

  /**
   * Icon per administrative level.
   *
   * A city and a municipality are different legal creatures — city status is conferred by law and read
   * from the register rather than inferred — so they are not given the same glyph.
   */
  protected icon(kind: string): IconName {
    return kind === 'City' ? 'lens-exposure' : 'locate';
  }
}
