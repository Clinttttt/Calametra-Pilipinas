import { ChangeDetectionStrategy, Component, computed, inject, output } from '@angular/core';

import { PLACE_RADII, PlaceStore } from '../../../core/places/place-store';
import type { PlaceEvent, PlaceMatch } from '../../../core/api/contracts';
import { Icon } from '../../../shared/ui/icon/icon';

/**
 * Search a place, then read what the archive holds around it.
 *
 * Three decisions carry the data-quality argument, and each answers a way this panel could
 * mislead.
 *
 * **No single largest nearby earthquake.** The strongest reading is listed once per magnitude
 * scale family, with how many readings each family holds. Around Surigao City that is Mb 6.0
 * from 228 body-wave readings, Mw 6.6 from 29 moment readings and Ms 6.7 from two surface-wave
 * ones — so the largest number belongs to the family with the fewest readings, and reducing
 * that to "largest: M6.7" would compare quantities that do not compare.
 *
 * **The raw count is not the headline.** The M6.0+ figure sits beside it, because detection
 * improved 285-fold across the last century while the M6.0+ rate stayed flat: a raw count for a
 * town says as much about seismometer coverage as about the ground beneath it.
 *
 * **The container is always shown.** 111 of the country's municipality names are not unique, so
 * a result reading only "San Isidro" would be a choice made blind.
 */
@Component({
  selector: 'cal-place-panel',
  standalone: true,
  imports: [Icon],
  templateUrl: './place-panel.html',
  styleUrl: './place-panel.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlacePanel {
  private readonly store = inject(PlaceStore);

  /** Raised when a place is chosen, so the camera can move to it. */
  readonly placeSelected = output<PlaceMatch>();

  /** Raised when one of the listed earthquakes is chosen. */
  readonly eventSelected = output<PlaceEvent>();

  readonly closed = output<void>();

  protected readonly term = this.store.term;
  protected readonly matches = this.store.matches;
  protected readonly searching = this.store.searching;
  protected readonly searched = this.store.searched;
  protected readonly selected = this.store.selected;
  protected readonly context = this.store.context;
  protected readonly loadingContext = this.store.loadingContext;
  protected readonly failed = this.store.failed;
  protected readonly radiusKm = this.store.radiusKm;

  protected readonly radii = PLACE_RADII;

  /**
   * True when the chosen place carries no PSGC code, so its context cannot be opened.
   *
   * Five places are in this state and each for a recorded reason: Cotabato City has no child
   * municipalities to derive a code from, Basilan and the Bangsamoro region split across two
   * region prefixes because Isabela City is administered under Region IX, and the two halves of
   * Maguindanao share the undivided province's code because the 2022 division postdates it.
   */
  protected readonly notAddressable = computed(
    () => this.selected() !== null && this.selected()!.psgcCode === null,
  );

  /** The attribution for the fault traces, stated once rather than per row. */
  protected readonly faultAttribution = computed(() => {
    const faults = this.context()?.nearestFaults ?? [];

    return faults.length === 0 ? null : faults[0].attribution;
  });

  protected search(value: string): void {
    this.store.search(value);
  }

  protected select(place: PlaceMatch): void {
    this.store.select(place);
    this.placeSelected.emit(place);
  }

  protected setRadius(km: number): void {
    this.store.setRadiusKm(km);
  }

  protected locate(event: PlaceEvent): void {
    this.eventSelected.emit(event);
  }

  protected back(): void {
    this.store.clearSelection();
  }

  protected close(): void {
    this.store.closePanel();
    this.closed.emit();
  }

  /** `Municipality` → `Mun.` is not worth the ambiguity; the level is shown in full. */
  protected formatDate(iso: string): string {
    return new Date(iso).toLocaleDateString('en-PH', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    });
  }

  protected formatYear(iso: string): string {
    return new Date(iso).getUTCFullYear().toString();
  }

  /** `BodyWave` → `body-wave`, so the family reads as prose rather than as an enum. */
  protected formatFamily(family: string): string {
    switch (family) {
      case 'BodyWave':
        return 'body-wave';
      case 'SurfaceWave':
        return 'surface-wave';
      case 'Moment':
        return 'moment';
      case 'Local':
        return 'local';
      default:
        return family.toLowerCase();
    }
  }
}
