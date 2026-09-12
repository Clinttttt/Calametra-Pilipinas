import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

import { placeOnScale } from '../../../core/visual/hazard-class-scale';
import { Icon } from '../../../shared/ui/icon/icon';
import type { HazardFeatureAttributes } from '../../../core/api/contracts';

/**
 * The publisher's own record for the feature under the pointer.
 *
 * <b>Its own component rather than markup inside the map.</b> Two reasons, and the second is the
 * substantive one. `explore.scss` sits at its stylesheet budget, and an ordinal scale needs a handful
 * of rules of its own — but more importantly this panel is the only place where a hazard *class* is
 * interpreted rather than plotted, and that reasoning is easier to hold in one file than scattered
 * through a map component.
 *
 * <b>What it adds beyond a table of attributes.</b> A susceptibility rating is an ordinal quantity:
 * printed as a word, "Least Susceptible" tells a reader nothing about whether it is the bottom of
 * four classes or the middle of seven, and it certainly does not say what the class means for the
 * ground under their feet. Placed on the publisher's own scale, with the publisher's own definition,
 * it says all three. Where no scale is held the attributes stand alone rather than being decorated
 * with an invented position.
 */
@Component({
  selector: 'cal-feature-inspector',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  templateUrl: './feature-inspector.html',
  styleUrl: './feature-inspector.scss',
})
export class FeatureInspector {
  readonly feature = input.required<HazardFeatureAttributes>();

  readonly closed = output<void>();

  /**
   * Attributes in the order the publisher returned them.
   *
   * Not sorted. A `keyvalue` pipe would order them alphabetically, which reorders an agency's fields
   * into an order it did not choose — and the first field is usually the one it considers the
   * headline.
   */
  protected readonly attributes = computed(() =>
    Object.entries(this.feature().attributes).map(([key, value]) => ({ key, value })),
  );

  /** The first attribute that names a class we hold a scale for, placed on it. */
  protected readonly classified = computed(() => {
    for (const { key, value } of this.attributes()) {
      const placement = placeOnScale(key, value);

      if (placement) {
        return { label: key, value, placement };
      }
    }

    return null;
  });

  protected close(): void {
    this.closed.emit();
  }
}
