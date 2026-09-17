import { DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, effect, inject, output, signal } from '@angular/core';

import { CalametraApi } from '../../../core/api/calametra-api';
import {
  type AdministrativeUnit,
  type LguEarthquakeContainment,
  type ProblemDetails,
} from '../../../core/api/contracts';
import { Icon } from '../../../shared/ui/icon/icon';
import { type IconName } from '../../../shared/ui/icon/icon-paths';
import { LguSelectionStore } from '../../../core/administrative/lgu-selection-store';
import { LguEarthquakeMapScopeStore } from '../../../core/earthquakes/lgu-earthquake-map-scope-store';

/**
 * What the reader selected, and what that selection does and does not mean.
 *
 * **Deliberately not the place panel.** ADR-005 D1 keeps containment and proximity apart: this names the
 * administrative unit a point falls in, while the place panel answers how much of the archive lies within
 * a distance of one representative point. Selecting a municipality here does not open a radius, and the
 * panel says so — otherwise a reader would reasonably assume the two are the same question asked twice.
 *
 * The boundary area is shown because ADR-005 D1 requires a containment figure to state the area of the
 * actual geometry used. It is a measurement of the mapped COD-AB polygon, not an official statistical or
 * cadastral LGU area; those are separate, independently sourced concepts.
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
  protected readonly mapScope = inject(LguEarthquakeMapScopeStore);
  private readonly api = inject(CalametraApi);

  readonly mapRequested = output<void>();

  /** Local, hazard-specific request state. It never reads or mutates the place/radius store. */
  protected readonly earthquakeContainment = signal<EarthquakeContainmentState>({ status: 'idle' });

  /** Administrative identity enrichment; independent from containment and from PlaceStore. */
  protected readonly administrativeUnit = signal<AdministrativeUnitState>({ status: 'idle' });

  /** Presentation state only; every newly selected administrative unit starts with details collapsed. */
  protected readonly containmentDetailsExpanded = signal(false);
  protected readonly boundaryDetailsExpanded = signal(false);

  constructor() {
    effect((onCleanup) => {
      const selected = this.store.selected();

      this.containmentDetailsExpanded.set(false);
      this.boundaryDetailsExpanded.set(false);

      if (selected === null) {
        this.earthquakeContainment.set({ status: 'idle' });
        this.administrativeUnit.set({ status: 'idle' });
        return;
      }

      this.earthquakeContainment.set({ status: 'loading' });
      this.administrativeUnit.set({ status: 'loading' });

      const containmentSubscription = this.api.getLguEarthquakeContainment(selected.psgc).subscribe({
        next: (summary) => this.earthquakeContainment.set({ status: 'ready', summary }),
        error: (error: unknown) =>
          this.earthquakeContainment.set({
            status: isBoundaryUnavailable(error) ? 'unavailable' : 'failed',
          }),
      });

      const administrativeSubscription = this.api.getAdministrativeUnit(selected.psgc).subscribe({
        next: (unit) => this.administrativeUnit.set({ status: 'ready', unit }),
        error: () => this.administrativeUnit.set({ status: 'failed' }),
      });

      // Selection can change while a request is in flight. Cancelling prevents the previous LGU's count
      // from replacing the current one when responses arrive out of order.
      onCleanup(() => {
        containmentSubscription.unsubscribe();
        administrativeSubscription.unsubscribe();
      });
    });
  }

  /**
   * Icon per administrative level.
   *
   * A city and a municipality are different legal creatures — city status is conferred by law and read
   * from the register rather than inferred — so they are not given the same glyph.
   */
  protected icon(kind: string): IconName {
    return kind === 'City' ? 'lens-exposure' : 'locate';
  }

  protected toggleContainmentDetails(): void {
    this.containmentDetailsExpanded.update((expanded) => !expanded);
  }

  protected toggleBoundaryDetails(): void {
    this.boundaryDetailsExpanded.update((expanded) => !expanded);
  }

  protected toggleMapScope(): void {
    // Explore owns the temporary archive-filter snapshot as well as map isolation state, so both entry
    // and exit are delegated to the map context rather than partially handled in this panel.
    this.mapRequested.emit();
  }

  protected registerEditionDisplay(label: string): string {
    return label.replace(/^PSGC\s+/i, '').replace(/\s+\([^)]*\)$/, '');
  }
}

type EarthquakeContainmentState =
  | { readonly status: 'idle' }
  | { readonly status: 'loading' }
  | { readonly status: 'ready'; readonly summary: LguEarthquakeContainment }
  | { readonly status: 'unavailable' }
  | { readonly status: 'failed' };

type AdministrativeUnitState =
  | { readonly status: 'idle' }
  | { readonly status: 'loading' }
  | { readonly status: 'ready'; readonly unit: AdministrativeUnit }
  | { readonly status: 'failed' };

function isBoundaryUnavailable(error: unknown): boolean {
  if (!(error instanceof HttpErrorResponse) || error.status !== 404) {
    return false;
  }

  return (error.error as ProblemDetails | null)?.code === 'lgu_boundary.not_available';
}
