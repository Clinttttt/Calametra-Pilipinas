import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';

import { CycloneStore } from '../../../core/cyclones/cyclone-store';
import type { AgencyTrack, CycloneOrder, CycloneSummary } from '../../../core/api/contracts';
import { categoryLabel, intensityLegend } from '../../../core/visual/cyclone-intensity';
import { Icon } from '../../../shared/ui/icon/icon';

/**
 * Tropical cyclone tracks.
 *
 * Two things this panel is careful about, both inherited from the data rather than chosen:
 *
 * Peak intensity is listed once per averaging period, never as one figure. A storm's strongest
 * number belongs to whichever agency uses the shortest averaging interval, and presenting it
 * alone would make JTWC's one-minute reading look like the storm's strength.
 *
 * The colour ramp is keyed to reported wind speed, not to a category. Saffir–Simpson is defined
 * on a one-minute wind, so the category name is shown only for the agency that reports one —
 * see `cyclone-intensity.ts`.
 *
 * One agency is emphasised at a time, and the others are drawn dashed rather than hidden. Four
 * tracks of equal weight over a dense earthquake field cannot be read, but hiding the rest
 * would present one agency's path as the storm's actual track.
 */
@Component({
  selector: 'cal-cyclone-panel',
  standalone: true,
  imports: [Icon],
  templateUrl: './cyclone-panel.html',
  styleUrl: './cyclone-panel.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CyclonePanel {
  private readonly store = inject(CycloneStore);

  protected readonly storms = this.store.storms;
  protected readonly loadingStorms = this.store.loadingStorms;
  protected readonly track = this.store.track;
  protected readonly loadingTrack = this.store.loadingTrack;
  protected readonly emphasised = this.store.emphasised;
  protected readonly currentFix = this.store.currentFix;
  protected readonly fixIndex = this.store.fixIndex;
  protected readonly fixCount = this.store.fixCount;
  protected readonly playing = this.store.playing;
  protected readonly season = this.store.season;
  protected readonly order = this.store.order;
  protected readonly query = this.store.query;
  protected readonly seasons = this.store.seasons;

  /**
   * True when the emphasised agency shares its averaging period with another.
   *
   * Worth marking, because those two peaks *are* comparable and any difference between them is
   * a genuine disagreement about the storm rather than a difference of method.
   */
  protected readonly hasComparablePeer = computed(() => {
    const tracks = this.track()?.tracks ?? [];
    const current = this.emphasised();

    if (current === null) {
      return false;
    }

    return tracks.some(
      (candidate) =>
        candidate.sourceSlug !== current.sourceSlug
        && candidate.averagingPeriod === current.averagingPeriod
        && candidate.peakWindKnots !== null,
    );
  });

  /**
   * The intensity ramp, weakest first so it reads left to right like a scale.
   *
   * Built from the same table the map paints from, so the key and the track cannot disagree.
   */
  protected readonly legend = computed(() => [...intensityLegend()].reverse());

  /**
   * The Saffir–Simpson label for the emphasised agency's peak, where one applies.
   *
   * Null for every agency except JTWC. The scale is defined on a one-minute sustained wind, and
   * naming a category from a ten-minute reading would state a fact the reading cannot support.
   */
  protected readonly emphasisedCategory = computed(() => {
    const current = this.emphasised();

    return current === null ? null : categoryLabel(current.peakWindKnots, current.averagingPeriod);
  });

  protected selectSeason(value: string): void {
    this.store.setSeason(value === 'all' ? null : Number(value));
  }

  protected selectOrder(order: CycloneOrder): void {
    this.store.setOrder(order);
  }

  /** Debouncing lives in the store, so the template can bind straight to the input event. */
  protected search(value: string): void {
    this.store.setQuery(value);
  }

  protected selectStorm(storm: CycloneSummary): void {
    this.store.selectStorm(storm.id);
  }

  protected emphasise(track: AgencyTrack): void {
    this.store.emphasise(track.sourceSlug);
  }

  protected scrub(value: string): void {
    this.store.setFixIndex(Number(value));
  }

  protected step(delta: number): void {
    this.store.step(delta);
  }

  protected togglePlayback(): void {
    this.store.togglePlayback();
  }

  protected back(): void {
    this.store.clearTrack();
  }

  protected close(): void {
    this.store.closePanel();
  }

  protected formatDate(iso: string): string {
    return new Date(iso).toLocaleDateString('en-PH', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    });
  }

  /** Fix timestamps are UTC and shown as such: a best-track fix is not a local observation. */
  protected formatFixTime(iso: string): string {
    return `${new Date(iso).toLocaleString('en-PH', {
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      timeZone: 'UTC',
      hour12: false,
    })} UTC`;
  }

  /** Knots to km/h, the unit PAGASA uses for public warnings. */
  protected kilometresPerHour(knots: number | null): number | null {
    return knots === null ? null : Math.round(knots * 1.852);
  }
}
