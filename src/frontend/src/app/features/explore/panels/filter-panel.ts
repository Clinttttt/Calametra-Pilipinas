import { ChangeDetectionStrategy, Component, computed, inject, output } from '@angular/core';

import {
  EarthquakeFilterStore,
  type TimePreset,
} from '../../../core/earthquakes/earthquake-filter-store';
import { Icon } from '../../../shared/ui/icon/icon';

/** A preset and the words for it. Ordered coarse to fine, which is how a reader narrows. */
const PRESETS: readonly { readonly id: TimePreset; readonly label: string }[] = [
  { id: 'all', label: 'All time' },
  { id: 'five-years', label: '5 years' },
  { id: 'twelve-months', label: '12 months' },
  { id: 'this-year', label: 'This year' },
  { id: 'this-month', label: 'This month' },
];

/**
 * Magnitude floors offered as one click.
 *
 * Not a continuous slider. The catalogue holds effectively nothing below M4.0 anywhere in the
 * archipelago, so a slider's lower half would be dead travel that implies data exists there; and
 * M6.0 is the one threshold with a stated meaning on this platform — the only band comparable
 * between eras.
 */
const MAGNITUDE_FLOORS: readonly (number | null)[] = [null, 4, 5, 6, 7];

/**
 * Depth bands offered as one click, in kilometres.
 *
 * The boundaries are the seismological ones the depth ramp already encodes, not round numbers: 70 km
 * separates crustal from intermediate, 300 km intermediate from deep. The archive reaches 667 km.
 */
const DEPTH_BANDS: readonly { readonly label: string; readonly min: number | null; readonly max: number | null }[] = [
  { label: 'Any depth', min: null, max: null },
  { label: '0–70 km', min: 0, max: 70 },
  { label: '70–300 km', min: 70, max: 300 },
  { label: '300 km+', min: 300, max: null },
];

/**
 * Bounds the reader sets on the earthquake archive.
 *
 * <b>Separate from the Time Machine, and the distinction is deliberate.</b> The Time Machine answers
 * "play the record forward" — it owns an instant and a scrub. This answers "show me only this", and
 * its window persists while the scrubber moves inside it. Two upper bounds intersect rather than
 * compete.
 *
 * Every control is a fixed choice rather than free text. A numeric field invites bounds the archive
 * cannot honour — a magnitude of 2.5, a depth of 900 km — and answering a reasonable request with an
 * empty map teaches them the platform is broken rather than that the catalogue has limits.
 */
@Component({
  selector: 'cal-filter-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  templateUrl: './filter-panel.html',
  styleUrl: './filter-panel.scss',
})
export class FilterPanel {
  private readonly store = inject(EarthquakeFilterStore);

  readonly closed = output<void>();

  /** Emitted whenever a bound changes, so the map can re-apply its GPU-side predicate. */
  readonly changed = output<void>();

  protected readonly presets = PRESETS;
  protected readonly magnitudeFloors = MAGNITUDE_FLOORS;
  protected readonly depthBands = DEPTH_BANDS;

  protected readonly preset = this.store.preset;
  protected readonly minMagnitude = this.store.minMagnitude;
  protected readonly includeAssignedDepth = this.store.includeAssignedDepth;
  protected readonly isFiltered = this.store.isFiltered;

  /** Which band is active, matched on both edges so a custom range highlights nothing. */
  protected readonly activeDepthBand = computed(() => {
    const min = this.store.minDepthKm();
    const max = this.store.maxDepthKm();

    return DEPTH_BANDS.findIndex((band) => band.min === min && band.max === max);
  });

  protected applyPreset(preset: TimePreset): void {
    this.store.applyPreset(preset);
    this.changed.emit();
  }

  protected setMagnitudeFloor(floor: number | null): void {
    // The upper bound is left open: a reader asking for M6+ means "at least", and the largest events
    // are the ones they are least likely to want excluded.
    this.store.setMagnitudeRange(floor, null);
    this.changed.emit();
  }

  protected setDepthBand(index: number): void {
    const band = DEPTH_BANDS[index];

    this.store.setDepthRange(band.min, band.max);
    this.changed.emit();
  }

  protected toggleAssignedDepth(): void {
    this.store.setIncludeAssignedDepth(!this.store.includeAssignedDepth());
    this.changed.emit();
  }

  protected clear(): void {
    this.store.clear();
    this.changed.emit();
  }

  protected close(): void {
    this.closed.emit();
  }

  protected magnitudeLabel(floor: number | null): string {
    return floor === null ? 'Any' : `M${floor.toFixed(1)}+`;
  }
}
