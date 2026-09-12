import { ChangeDetectionStrategy, Component, computed, inject, output } from '@angular/core';

import { PLACE_RADII, PlaceStore } from '../../../core/places/place-store';
import type { EarthquakeSummary, PlaceEvent, PlaceMatch } from '../../../core/api/contracts';
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
  readonly eventSelected = output<{ readonly latitude: number; readonly longitude: number }>();

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

  /**
   * The full list of events in the radius, which the summary cards deliberately do not show.
   *
   * The cards answer "what is the strongest and the most recent". They were the whole panel, and a
   * reader seeing "443 within 50 km, catalogued 1913-2026" and three cards is right to ask where the
   * rest are. Paged rather than complete, with the total always stated so a page never reads as the
   * record.
   */
  protected readonly events = this.store.events;
  protected readonly eventsTotal = this.store.eventsTotal;
  protected readonly loadingEvents = this.store.loadingEvents;
  protected readonly hasMoreEvents = this.store.hasMoreEvents;

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

  /**
   * Locates a row from the full list.
   *
   * Emits the same shape as the summary cards' Locate, because the map only needs the position —
   * widening the output to the two fields it reads is cheaper and clearer than a second event for
   * the same action.
   */
  protected locateSummary(event: EarthquakeSummary): void {
    this.eventSelected.emit(event);
  }

  protected loadMore(): void {
    this.store.loadMoreEvents();
  }

  /**
   * A date for the dense list, as ISO.
   *
   * Deliberately different from `formatDate`, which writes prose dates for the summary cards. In a
   * column of 150 rows the value being scanned is the ordering, and `2026-08-25` is fixed-width in
   * the monospaced face where "Aug 25, 2026" is not — the ragged form is what pushed the row onto
   * three lines. It is also the form the catalogue itself uses.
   */
  protected formatListDate(iso: string): string {
    return iso.slice(0, 10);
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
