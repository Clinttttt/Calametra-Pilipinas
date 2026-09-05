import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

import { Icon } from '../../../shared/ui/icon/icon';
import { type EarthquakeDetail, type ObservationSummary } from '../../../core/api/contracts';
import { depthBandFor } from '../../../core/visual/depth-scale';

/**
 * Detail panel for a selected earthquake.
 *
 * This is the view the platform is built around. A conventional hazard application
 * shows one magnitude per earthquake, which forces an arbitrary choice between
 * agencies and hides that the choice was made. This panel shows every reading side
 * by side, each labelled with the agency that produced it and the scale it was
 * measured on, and explains why they differ.
 *
 * For the 10 February 2017 Surigao earthquake that means PHIVOLCS Ms 6.7 at 10 km
 * beside USGS Mww 6.5 at 15 km — neither wrong, and not directly comparable.
 *
 * Presentational only: the parent owns fetching, so this component has no
 * dependency on the API and can be rendered from a fixture.
 */
@Component({
  selector: 'cal-event-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  templateUrl: './event-detail.html',
  styleUrl: './event-detail.scss',
})
export class EventDetail {
  /** The loaded event, or null while loading or when nothing is selected. */
  readonly event = input<EarthquakeDetail | null>(null);

  readonly loading = input(false);

  /** True when the request failed, so the panel can say so rather than look empty. */
  readonly failed = input(false);

  readonly closed = output<void>();

  /** Fly the map to a specific agency's epicentre. */
  readonly locateRequested = output<ObservationSummary>();

  /** The reading shown as the headline figure. */
  protected readonly preferred = computed<ObservationSummary | null>(() => {
    const observations = this.event()?.observations ?? [];

    return observations.find((observation) => observation.isPreferred) ?? observations[0] ?? null;
  });

  protected readonly observations = computed(() => this.event()?.observations ?? []);

  /** Whether to render the comparison block at all. */
  protected readonly hasMultiple = computed(() => this.observations().length > 1);

  /**
   * Agencies whose epicentres differ enough to be worth locating separately.
   *
   * Sources routinely place the same event tens of kilometres apart, which is itself
   * information — but only worth surfacing when there is more than one to compare.
   */
  protected readonly epicentreSpreadKm = computed(() => {
    const observations = this.observations();

    if (observations.length < 2) {
      return null;
    }

    let maxKm = 0;

    for (let i = 0; i < observations.length - 1; i++) {
      for (let j = i + 1; j < observations.length; j++) {
        maxKm = Math.max(maxKm, distanceKm(observations[i], observations[j]));
      }
    }

    // Below a kilometre the difference is not meaningful at map scale.
    return maxKm < 1 ? null : Math.round(maxKm);
  });

  protected readonly formattedTime = computed(() => {
    const occurredAt = this.event()?.occurredAt;

    if (!occurredAt) {
      return '';
    }

    return new Date(occurredAt).toLocaleString('en-PH', {
      dateStyle: 'full',
      timeStyle: 'short',
    });
  });

  protected depthBand(observation: ObservationSummary): string {
    return depthBandFor(observation.depth);
  }

  protected close(): void {
    this.closed.emit();
  }

  protected locate(observation: ObservationSummary): void {
    this.locateRequested.emit(observation);
  }
}

/** Great-circle distance between two reported epicentres, in kilometres. */
function distanceKm(a: ObservationSummary, b: ObservationSummary): number {
  const earthRadiusKm = 6371;
  const toRadians = (degrees: number) => (degrees * Math.PI) / 180;

  const dLat = toRadians(b.latitude - a.latitude);
  const dLon = toRadians(b.longitude - a.longitude);

  const h =
    Math.sin(dLat / 2) ** 2 +
    Math.cos(toRadians(a.latitude)) * Math.cos(toRadians(b.latitude)) * Math.sin(dLon / 2) ** 2;

  return earthRadiusKm * 2 * Math.asin(Math.sqrt(h));
}
