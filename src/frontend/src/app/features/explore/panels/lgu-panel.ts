import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

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
 */
@Component({
  selector: 'cal-lgu-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe],
  template: `
    @if (store.selected(); as lgu) {
      <aside class="lgu-panel" aria-label="Selected administrative unit">
        <header class="lgu-panel__header">
          <span class="c-label">Administrative unit</span>
          <button class="c-icon-button" type="button" (click)="store.clear()" aria-label="Clear">
            ×
          </button>
        </header>

        <p class="lgu-panel__name">{{ lgu.name }}</p>

        <dl class="lgu-panel__facts">
          <dt>Kind</dt>
          <dd>{{ lgu.kind }}</dd>
          <dt>PSGC</dt>
          <dd>{{ lgu.psgc }}</dd>
          <dt>Land area</dt>
          <dd>{{ lgu.areaSquareKm | number: '1.0-1' }} km²</dd>
        </dl>

        <p class="lgu-panel__note">
          Land outline only — not municipal waters. This names the unit, and is not a radius: distances
          and event counts elsewhere are measured from a representative point, which is a different
          question.
        </p>
      </aside>
    }
  `,
  styles: `
    .lgu-panel {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      padding: 0.75rem;
      background: color-mix(in srgb, var(--c-surface) 92%, transparent);
      border: 1px solid var(--c-border);
      border-radius: var(--c-radius-sm, 4px);
      max-width: 20rem;
    }

    .lgu-panel__header {
      display: flex;
      align-items: center;
      justify-content: space-between;
    }

    .lgu-panel__name {
      margin: 0;
      font-size: 1rem;
      font-weight: 600;
    }

    .lgu-panel__facts {
      display: grid;
      grid-template-columns: auto 1fr;
      gap: 0.15rem 0.75rem;
      margin: 0;
      font-size: 0.8125rem;
    }

    .lgu-panel__facts dt {
      color: var(--c-text-muted);
    }

    .lgu-panel__facts dd {
      margin: 0;
      font-variant-numeric: tabular-nums;
    }

    .lgu-panel__note {
      margin: 0;
      font-size: 0.75rem;
      line-height: 1.4;
      color: var(--c-text-muted);
    }
  `,
})
export class LguPanel {
  protected readonly store = inject(LguSelectionStore);
}
