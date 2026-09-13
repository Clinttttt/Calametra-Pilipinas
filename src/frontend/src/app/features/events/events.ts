import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { CalametraApi } from '../../core/api/calametra-api';
import { DecimalPipe } from '@angular/common';

import { Icon } from '../../shared/ui/icon/icon';
import type { EarthquakeQuery, EarthquakeSummary, MagnitudeReading } from '../../core/api/contracts';

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
  templateUrl: './events.html',
  styleUrl: './events.scss',
})
export class Events {
  private readonly api = inject(CalametraApi);

  protected readonly sorts = SORTS;
  protected readonly families = FAMILIES;

  protected readonly rows = signal<readonly EarthquakeSummary[]>([]);
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
    } finally {
      this.loading.set(false);
    }
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
