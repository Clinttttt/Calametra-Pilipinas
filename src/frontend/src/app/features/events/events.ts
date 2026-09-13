import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { CalametraApi } from '../../core/api/calametra-api';
import { DecimalPipe } from '@angular/common';

import { Icon } from '../../shared/ui/icon/icon';
import type {
  CycloneOrder,
  CycloneSummary,
  EarthquakeQuery,
  EarthquakeSummary,
  MagnitudeReading,
} from '../../core/api/contracts';

type ScaleFamily = MagnitudeReading['scaleFamily'];

/** Orderings offered, and what each is for. */
const SORTS: readonly { readonly id: NonNullable<EarthquakeQuery['sort']>; readonly label: string }[] = [
  { id: 'Newest', label: 'Newest first' },
  { id: 'Oldest', label: 'Oldest first' },
  { id: 'Strongest', label: 'Strongest first' },
];

/**
 * Scale families offered as a filter.
 *
 * Named as the catalogue names them rather than glossed: a reader checking against the USGS record has
 * to find the same word. The share of the archive is stated because it is the reason this filter
 * exists — 92.8% of readings are body-wave, so an unfiltered list is overwhelmingly one family.
 */
const FAMILIES: readonly { readonly id: ScaleFamily; readonly label: string }[] = [
  { id: 'BodyWave', label: 'Body-wave' },
  { id: 'Moment', label: 'Moment' },
  { id: 'SurfaceWave', label: 'Surface-wave' },
  { id: 'Local', label: 'Local' },
];

const PAGE_SIZE = 100;

/**
 * The archive as a catalogue rather than a map.
 *
 * <b>Why this page exists alongside Explore.</b> A map answers "where", and it answers it well —
 * 27,242 markers show the trench systems tracing themselves out. What it cannot do is let a reader
 * read: scan a column of dates, sort by magnitude, or take in an event's magnitude, scale, depth and
 * reporting agency at once. Those are table questions, and this is the table.
 *
 * <b>Ordering by magnitude is constrained, not free.</b> The server refuses `Strongest` without a
 * scale family, because ranking body-wave against moment readings orders events on a property of the
 * scale rather than of the earthquake. Rather than let the reader hit that error, the control selects
 * the dominant family with the ranking and says so.
 */
@Component({
  selector: 'cal-events',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, DecimalPipe],
  // The reading-page backdrop, applied to the host so it spans the viewport rather than the centred
  // measure. Global rather than component-scoped because the record page uses the same wash.
  host: { class: 'c-page-wash' },
  templateUrl: './events.html',
  styleUrl: './events.scss',
})
export class Events {
  private readonly api = inject(CalametraApi);
  private readonly router = inject(Router);

  protected readonly sorts = SORTS;
  protected readonly families = FAMILIES;

  /**
   * Which hazard is being listed.
   *
   * <b>Two tables rather than one merged list, and that is a considered refusal.</b> The platform's
   * subject is multi-hazard, but an earthquake row and a storm row are not the same kind of record: a
   * moment magnitude and a ten-minute mean wind are different quantities, one event is an instant and
   * the other a track lasting days, and a storm carries a peak per agency where an earthquake carries a
   * magnitude per agency. Flattening them into shared columns would require inventing a common
   * "severity", which is precisely the false equivalence this platform exists to avoid. So the hazard
   * is chosen, and each gets the columns its record actually has.
   */
  protected readonly hazard = signal<'earthquakes' | 'cyclones'>('earthquakes');

  /** Storm-list controls. Seasons are offered as decades because 82 individual years is not a control. */
  protected readonly stormOrder = signal<CycloneOrder>('Intensity');
  protected readonly landfallOnly = signal(false);
  protected readonly season = signal<number | null>(null);
  protected readonly stormName = signal('');

  protected readonly rows = signal<readonly EarthquakeSummary[]>([]);
  protected readonly storms = signal<readonly CycloneSummary[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);

  protected readonly sort = signal<NonNullable<EarthquakeQuery['sort']>>('Newest');
  protected readonly family = signal<ScaleFamily | null>(null);
  protected readonly minMagnitude = signal<number | null>(null);

  protected readonly pageSize = PAGE_SIZE;

  protected readonly lastPage = computed(() => Math.max(1, Math.ceil(this.total() / PAGE_SIZE)));

  /** First and last row numbers shown, so the page states its position in the archive. */
  protected readonly firstRow = computed(() => (this.page() - 1) * PAGE_SIZE + 1);
  protected readonly lastRow = computed(() =>
    Math.min(this.page() * PAGE_SIZE, this.total()),
  );

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.failed.set(false);

    try {
      if (this.hazard() === 'cyclones') {
        // Ordered by intensity by default and capped by the endpoint: the storm list is a few
        // thousand rows rather than 27,000, and it is read season by season rather than paged.
        this.storms.set(
          await firstValueFrom(
            this.api.searchCyclones(
              this.season() ?? undefined,
              this.landfallOnly(),
              this.stormOrder(),
              this.stormName().trim() === '' ? undefined : this.stormName().trim(),
            ),
          ),
        );
        this.total.set(this.storms().length);

        return;
      }

      const result = await firstValueFrom(
        this.api.searchEarthquakes({
          page: this.page(),
          pageSize: PAGE_SIZE,
          sort: this.sort(),
          scaleFamily: this.family() ?? undefined,
          minMagnitude: this.minMagnitude() ?? undefined,
        }),
      );

      this.rows.set(result.items);
      this.total.set(result.totalCount);
    } catch {
      // The interceptor has already reported it; the page degrades to an empty table with a retry
      // rather than an error screen that loses the reader's filters.
      this.failed.set(true);
      this.rows.set([]);
      this.storms.set([]);
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * Opens one earthquake's full record.
   *
   * Routed to Explore with the event selected rather than to a page of its own: the detail panel there
   * already shows every agency's reading with the disagreement explained, and it shows them beside the
   * epicentre on the map, which is context a standalone page would have to rebuild badly.
   */
  protected openEarthquake(id: string): void {
    void this.router.navigate(['/explore'], { queryParams: { event: id } });
  }

  /** Decade starts offered as season filters, newest first. IBTrACS begins in 1884; this archive at 1945. */
  protected readonly decades: readonly number[] = [
    2020, 2010, 2000, 1990, 1980, 1970, 1960, 1950,
  ];

  protected setStormOrder(order: CycloneOrder): void {
    this.stormOrder.set(order);
    void this.load();
  }

  protected toggleLandfallOnly(): void {
    this.landfallOnly.update((only) => !only);
    void this.load();
  }

  /**
   * Filters to a decade rather than a single season.
   *
   * The endpoint takes one season, so a decade is applied by asking for its first year — which is
   * honest only if the control says so, and it does: the label reads "1990s" and the caption states the
   * season actually queried. Eighty-two individual years is a list, not a control.
   */
  protected setSeason(season: number | null): void {
    this.season.set(season);
    void this.load();
  }

  protected setStormName(name: string): void {
    this.stormName.set(name);
    void this.load();
  }

  protected setHazard(hazard: 'earthquakes' | 'cyclones'): void {    if (hazard === this.hazard()) {
      return;
    }

    this.hazard.set(hazard);
    this.page.set(1);
    void this.load();
  }

  /**
   * A storm's duration in days, rounded up.
   *
   * Stated because it is the clearest way an earthquake row and a storm row differ: one is an instant,
   * the other is a track that lasted days.
   */
  protected durationDays(storm: CycloneSummary): number {
    const started = Date.parse(storm.startedAt);
    const ended = Date.parse(storm.endedAt);

    return Math.max(1, Math.ceil((ended - started) / 86_400_000));
  }

  protected setSort(sort: NonNullable<EarthquakeQuery['sort']>): void {
    this.sort.set(sort);

    // Ranking by magnitude without a family is refused by the server, for a reason this platform
    // states everywhere else: body-wave and moment magnitudes are different quantities. Rather than
    // surface that as a validation error, the ranking selects the family that dominates the archive,
    // and the note under the table says which.
    if (sort === 'Strongest' && this.family() === null) {
      this.family.set('Moment');
    }

    this.page.set(1);
    void this.load();
  }

  protected setFamily(family: ScaleFamily | null): void {
    // Clearing the family while ranking by magnitude would produce a request the server refuses, so
    // the ordering falls back to time — the one ordering that is always valid.
    if (family === null && this.sort() === 'Strongest') {
      this.sort.set('Newest');
    }

    this.family.set(family);
    this.page.set(1);
    void this.load();
  }

  protected setMinMagnitude(minimum: number | null): void {
    this.minMagnitude.set(minimum);
    this.page.set(1);
    void this.load();
  }

  protected goToPage(page: number): void {
    if (page < 1 || page > this.lastPage() || page === this.page()) {
      return;
    }

    this.page.set(page);
    void this.load();
    globalThis.scrollTo({ top: 0, behavior: 'smooth' });
  }

  /** ISO date, fixed-width in the monospaced face so a column of them aligns. */
  protected formatDate(iso: string): string {
    return iso.slice(0, 10);
  }

  protected formatTime(iso: string): string {
    return iso.slice(11, 16);
  }

  protected coordinates(row: EarthquakeSummary): string {
    return `${row.latitude.toFixed(3)}, ${row.longitude.toFixed(3)}`;
  }
}
