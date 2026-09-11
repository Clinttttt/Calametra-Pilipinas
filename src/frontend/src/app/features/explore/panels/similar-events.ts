import { ChangeDetectionStrategy, Component, computed, inject, output } from '@angular/core';

import { SimilarEventsStore } from '../../../core/similar-events/similar-events-store';
import type { SimilarEarthquake } from '../../../core/api/contracts';
import { Icon } from '../../../shared/ui/icon/icon';

/**
 * Earthquakes resembling the selected one.
 *
 * Shows the component breakdown per match rather than a bare score. A single percentage
 * invites a reader to treat similarity as a measurement; naming the three comparisons —
 * and saying plainly when one could not be made — keeps it a stated claim.
 *
 * The panel leads with the exclusion counts, not the matches. Under the strict defaults a
 * flagship event can return one result, and the reason is the point: 3,612 of the 3,633
 * earthquakes within 150 km of the 2017 Surigao epicentre report a magnitude that cannot
 * be compared with PHIVOLCS's Ms 6.7.
 */
@Component({
  selector: 'cal-similar-events',
  standalone: true,
  imports: [Icon],
  templateUrl: './similar-events.html',
  styleUrl: './similar-events.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SimilarEvents {
  private readonly store = inject(SimilarEventsStore);

  /** Raised when a match is chosen, so the map can move to it. */
  readonly matchSelected = output<SimilarEarthquake>();

  /** Raised when a match should be set against the reference event. */
  readonly compareRequested = output<SimilarEarthquake>();

  readonly closed = output<void>();

  protected readonly result = this.store.result;
  protected readonly loading = this.store.loading;
  protected readonly failed = this.store.failed;
  protected readonly hasRun = this.store.hasRun;
  protected readonly setAside = this.store.setAside;
  protected readonly maxDistanceKm = this.store.maxDistanceKm;
  protected readonly requireComparableScale = this.store.requireComparableScale;
  protected readonly requireMeasuredDepth = this.store.requireMeasuredDepth;

  /** Radii offered. Stops well short of the server's 300 km cap. */
  protected readonly distanceOptions = [50, 100, 150, 250] as const;

  protected readonly matches = computed(() => this.result()?.matches ?? []);

  /**
   * True when the result is empty because the filters removed everything, rather than
   * because nothing is nearby. The two need different wording.
   */
  protected readonly emptyByFilter = computed(() => {
    const result = this.result();

    return result !== null && result.matches.length === 0 && result.nearbyEvents > 0;
  });

  /**
   * True when no match could be compared on depth at all.
   *
   * Worth calling out, because the cause is usually the reference event rather than the
   * candidates. If the reference's own depth was assigned by the agency it cannot be
   * differenced against anything, so every row reads "not measured" and the reader is left
   * to infer why from a column of identical cells.
   *
   * When `requireMeasuredDepth` is on, every candidate is guaranteed to carry a measured
   * depth — so a universal failure can only come from the reference, and the note says so
   * definitively rather than hedging.
   */
  protected readonly depthNeverComparable = computed(() => {
    const matches = this.matches();

    return matches.length > 0 && matches.every((match) => match.depthDeltaKm === null);
  });

  /**
   * The single agency behind every match, or null when they differ.
   *
   * USGS supplies almost the whole catalogue, so repeating its full name on each card is
   * five lines of identical text competing with the figures. Stated once when uniform and
   * per row only when it actually varies — provenance is a platform invariant, so the
   * answer is to say it well rather than to drop it.
   */
  protected readonly sharedAgency = computed(() => {
    const matches = this.matches();

    if (matches.length === 0) {
      return null;
    }

    const first = matches[0].agency;

    return matches.every((match) => match.agency === first) ? first : null;
  });

  protected search(): void {
    this.store.search();
  }

  protected setDistance(km: number): void {
    this.store.setMaxDistanceKm(km);
  }

  protected toggleComparableScale(): void {
    this.store.setRequireComparableScale(!this.requireComparableScale());
  }

  protected toggleMeasuredDepth(): void {
    this.store.setRequireMeasuredDepth(!this.requireMeasuredDepth());
  }

  protected select(match: SimilarEarthquake): void {
    this.matchSelected.emit(match);
  }

  protected compare(match: SimilarEarthquake): void {
    this.compareRequested.emit(match);
  }

  protected close(): void {
    this.store.close();
    this.closed.emit();
  }

  /** Score as a whole percentage. The breakdown beneath it carries the detail. */
  protected percentage(score: number): number {
    return Math.round(score * 100);
  }

  /**
   * A component difference, or a statement that it could not be measured.
   *
   * Three cases, because a bare `±0` reads as a placeholder rather than as information:
   *
   * - **null** returns the supplied wording. An unavailable comparison is not a difference
   *   of zero, and must never be rendered as a number.
   * - **exactly zero** returns "identical". Magnitudes are reported to one decimal, so this
   *   is genuine equality and worth saying plainly — it is the strongest possible match on
   *   that component.
   * - **below the displayed precision** returns "&lt;0.1" rather than rounding to `±0.0`,
   *   which would claim an equality the figures do not support.
   */
  protected formatDelta(delta: number | null, unavailable: string, unit = ''): string {
    if (delta === null) {
      return unavailable;
    }

    if (delta === 0) {
      return 'identical';
    }

    return delta < 0.05 ? `<0.1${unit}` : `±${delta.toFixed(1)}${unit}`;
  }

  protected formatDate(iso: string): string {
    return new Date(iso).toLocaleDateString('en-PH', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    });
  }
}
