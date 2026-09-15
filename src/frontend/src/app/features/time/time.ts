import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe, PercentPipe } from '@angular/common';
import { firstValueFrom } from 'rxjs';

import { CalametraApi } from '../../core/api/calametra-api';
import { BasemapStore } from '../../core/basemap/basemap-store';
import { Icon } from '../../shared/ui/icon/icon';
import { PageBackdrop } from '../../shared/ui/page-backdrop/page-backdrop';
import type { CatalogueCompleteness, DecadeSummary } from '../../core/api/contracts';

/**
 * The catalogue's own history — what can and cannot be compared across eras.
 *
 * <b>Why this is a page and not a caption.</b> The Explore timeline lets a reader scrub through the
 * archive, and doing so shows the record thickening around 1970. What the scrubber cannot say is
 * *why*, and the wrong conclusion is the easy one: that the Philippines has become more seismic. This
 * page puts the two series side by side — every reading per decade, and the magnitude-6 rate per year
 * — because the first rises 285-fold while the second stays flat, and that pairing is the whole
 * argument.
 *
 * Nothing here is asserted in prose that is not also computed: the bars, the rates and the shares all
 * come from `GET /api/earthquakes/completeness`, so the page cannot drift from the archive it
 * describes.
 */
@Component({
  selector: 'cal-time',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, PageBackdrop, DecimalPipe, PercentPipe],
  host: { class: 'c-page-wash' },
  templateUrl: './time.html',
  styleUrl: './time.scss',
})
export class TimeView {
  private readonly api = inject(CalametraApi);
  private readonly basemaps = inject(BasemapStore);

  /**
   * The credit the active base layer requires, printed in the page's own flow.
   *
   * Read from the store rather than written here, so it changes with the reader's choice and cannot
   * drift from the source actually being fetched.
   */
  protected readonly basemapCredit = computed(() => this.basemaps.selected().attribution);

  protected readonly data = signal<CatalogueCompleteness | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  /** Which series the bars show. Both are drawn from the same rows; only the scale differs. */
  protected readonly series = signal<'total' | 'comparable'>('total');

  protected readonly decades = computed(() => this.data()?.decades ?? []);

  /**
   * The tallest bar in the active series, used to scale the rest.
   *
   * Scaled per series rather than shared: drawn against a common maximum the magnitude-6 bars would be
   * a flat line of two-pixel stubs, which reads as "nothing happened" instead of "this rate is
   * steady". The axis label states which series is being scaled.
   */
  protected readonly peak = computed(() => {
    const values = this.decades().map((decade) => this.value(decade));

    return values.length === 0 ? 1 : Math.max(...values, 1);
  });

  /** The ratio the page exists to state, computed rather than written down. */
  protected readonly growth = computed(() => {
    const decades = this.decades();

    if (decades.length < 2) {
      return null;
    }

    const first = decades[0];
    const last = decades.at(-1)!;

    return {
      firstDecade: first.decade,
      lastDecade: last.decade,
      totalFactor: first.totalCount === 0 ? null : Math.round(last.totalCount / first.totalCount),
      firstRate: first.comparablePerYear,
      lastRate: last.comparablePerYear,
    };
  });

  /**
   * The decades over which the comparable rate is itself steady.
   *
   * Stated from the data rather than claimed: the magnitude-6 rate is flat from the 1920s, but the
   * 1900s and 1910s sit well below it, which means even large events were being missed then. Writing
   * "the rate is flat across the century" would be the same species of error this page warns about.
   */
  protected readonly steadyFrom = computed(() => {
    const decades = this.decades();

    if (decades.length === 0) {
      return null;
    }

    const rates = decades.map((decade) => decade.comparablePerYear);
    const median = [...rates].sort((a, b) => a - b)[Math.floor(rates.length / 2)];

    // The first decade whose rate reaches four fifths of the median, and which is not followed by a
    // decade that falls below it again.
    const threshold = median * 0.8;
    const index = decades.findIndex((decade, position) =>
      decade.comparablePerYear >= threshold
      && decades.slice(position).every((later) => later.comparablePerYear >= threshold * 0.6),
    );

    return index < 0 ? null : decades[index].decade;
  });

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.failed.set(false);

    try {
      this.data.set(await firstValueFrom(this.api.getCatalogueCompleteness()));
    } catch {
      this.failed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  protected value(decade: DecadeSummary): number {
    return this.series() === 'total' ? decade.totalCount : decade.comparablePerYear;
  }

  protected barHeight(decade: DecadeSummary): number {
    // Linear, not square-rooted. The Explore histogram compresses its bars so a quiet month stays
    // visible; here the disproportion *is* the finding, so it is drawn at full scale.
    return Math.max(1, Math.round((this.value(decade) / this.peak()) * 100));
  }

  protected setSeries(series: 'total' | 'comparable'): void {
    this.series.set(series);
  }

  protected label(decade: DecadeSummary): string {
    return `${decade.decade}s`;
  }
}
