import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';

import { Icon } from '../../../shared/ui/icon/icon';
import { type ActivityBucket } from '../../../core/api/contracts';

/** Playback speeds, as years advanced per second. */
const SPEEDS = [0.5, 1, 2, 5] as const;

type Speed = (typeof SPEEDS)[number];

/** One year of activity, aggregated from monthly buckets. */
interface YearBar {
  readonly year: number;
  readonly count: number;
  readonly maxMagnitude: number | null;
  readonly heightPercent: number;
  readonly isPast: boolean;
  readonly isSignificant: boolean;
  readonly isDecade: boolean;
}

/**
 * The Time Machine.
 *
 * ── Why yearly bars, not monthly ────────────────────────────────────────────
 * The archive spans 1901–2026, which is 1,497 monthly buckets. Drawn at monthly
 * resolution across a viewport, each bar is under a pixel wide and the axis labels
 * collapse into an unreadable smear. Aggregating to 126 yearly bars gives every bar
 * real width, makes the axis legible with one label per decade, and still shows the
 * distribution clearly — the 1970s instrumentation step and the 2023 sequence are
 * both obvious at yearly resolution.
 *
 * Monthly precision is not lost: the scrubber still resolves to a month, and the
 * readout states it. Only the *drawing* is aggregated.
 *
 * ── Why the distribution is drawn at all ────────────────────────────────────
 * Events per decade rise from 21 in the 1900s to 5,974 in the 2020s, a 285-fold
 * increase that is entirely a change in instrumentation. Over the same period the
 * M6.0+ rate is flat at roughly five per year. A bare scrubber would let a user
 * conclude that earthquakes are becoming more frequent, so the shape of the record
 * is shown and the comparable subset is one click away.
 */
@Component({
  selector: 'cal-timeline',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  templateUrl: './timeline.html',
  styleUrl: './timeline.scss',
})
export class Timeline {
  private readonly destroyRef = inject(DestroyRef);

  /** Magnitude floor above which the record is consistent across eras. */
  private static readonly comparableMagnitude = 6;

  readonly buckets = input<readonly ActivityBucket[]>([]);

  readonly totalCount = input(0);

  /** Emits the currently selected instant. Null means "show everything". */
  readonly instantChanged = output<number | null>();

  /** Emits a magnitude floor when the comparable-record view is on. */
  readonly magnitudeFloorChanged = output<number | null>();

  protected readonly speeds = SPEEDS;
  protected readonly playing = signal(false);
  protected readonly speed = signal<Speed>(2);
  protected readonly comparableOnly = signal(false);

  /** Position along the timeline, 0–1. */
  protected readonly position = signal(1);

  protected readonly showingAll = computed(() => this.position() >= 1);

  /** Years present in the archive, ascending. */
  private readonly years = computed(() => {
    const totals = new Map<number, { count: number; maxMagnitude: number | null }>();

    for (const bucket of this.buckets()) {
      const year = new Date(Date.parse(bucket.periodStart)).getUTCFullYear();
      const existing = totals.get(year);

      totals.set(year, {
        count: (existing?.count ?? 0) + bucket.count,
        maxMagnitude: Math.max(existing?.maxMagnitude ?? 0, bucket.maxMagnitude ?? 0) || null,
      });
    }

    return [...totals.entries()]
      .map(([year, value]) => ({ year, ...value }))
      .sort((a, b) => a.year - b.year);
  });

  protected readonly firstYear = computed(() => this.years()[0]?.year ?? 0);

  protected readonly lastYear = computed(() => this.years().at(-1)?.year ?? 0);

  private readonly startMs = computed(() =>
    this.years().length === 0 ? 0 : Date.UTC(this.firstYear(), 0, 1),
  );

  private readonly endMs = computed(() =>
    this.years().length === 0 ? 0 : Date.UTC(this.lastYear() + 1, 0, 1),
  );

  protected readonly currentMs = computed(
    () => this.startMs() + (this.endMs() - this.startMs()) * this.position(),
  );

  protected readonly currentYear = computed(() =>
    this.years().length === 0 ? 0 : new Date(this.currentMs()).getUTCFullYear(),
  );

  /** Month-precision readout, so aggregating the drawing does not coarsen the label. */
  protected readonly currentLabel = computed(() => {
    if (this.years().length === 0) {
      return '—';
    }

    if (this.showingAll()) {
      return 'Complete archive';
    }

    return new Date(this.currentMs()).toLocaleDateString('en-PH', {
      year: 'numeric',
      month: 'long',
    });
  });

  /**
   * True while the scrubber sits in the sparsely-recorded era.
   *
   * Warns in place rather than only in documentation: this is the point at which a
   * user would otherwise read an empty-looking map as "few earthquakes" instead of
   * "few seismometers".
   */
  protected readonly inSparseEra = computed(
    () => !this.showingAll() && this.years().length > 0 && this.currentYear() < 1970,
  );

  protected readonly bars = computed<YearBar[]>(() => {
    const years = this.years();

    if (years.length === 0) {
      return [];
    }

    const peak = Math.max(1, ...years.map((year) => year.count));
    const currentYear = this.currentYear();
    const showingAll = this.showingAll();

    return years.map((year) => ({
      year: year.year,
      count: year.count,
      maxMagnitude: year.maxMagnitude,
      // Square root, not linear: the quietest years hold ~10 events against a peak
      // near 1,000, which is 1% of the height on a linear scale — invisible. The
      // root brings it to 10%: clearly smaller, still present.
      heightPercent: Math.max(3, Math.sqrt(year.count / peak) * 100),
      isPast: showingAll || year.year <= currentYear,
      // A year containing a major event is marked, because a count alone hides it:
      // 1918 holds one M8.3 and little else.
      isSignificant: (year.maxMagnitude ?? 0) >= 7.5,
      isDecade: year.year % 10 === 0,
    }));
  });

  /** Decade labels only. One per year is what made the axis unreadable. */
  protected readonly decades = computed(() =>
    this.bars()
      .map((bar, index) => ({ bar, index }))
      .filter(({ bar }) => bar.isDecade)
      .map(({ bar, index }) => ({
        year: bar.year,
        offsetPercent: ((index + 0.5) / this.bars().length) * 100,
      })),
  );

  constructor() {
    // Playback advances by year, at a fixed tick rather than per animation frame:
    // the map filter is the expensive part, and updating it 60 times a second costs
    // far more than it adds.
    effect((onCleanup) => {
      if (!this.playing()) {
        return;
      }

      const totalYears = Math.max(1, this.years().length);
      const tickMs = 120;
      const step = (this.speed() * (tickMs / 1000)) / totalYears;

      const handle = setInterval(() => {
        const next = this.position() + step;

        if (next >= 1) {
          this.position.set(1);
          this.playing.set(false);
        } else {
          this.position.set(next);
        }

        this.emit();
      }, tickMs);

      onCleanup(() => clearInterval(handle));
    });

    this.destroyRef.onDestroy(() => this.playing.set(false));
  }

  protected togglePlay(): void {
    // Pressing play on a finished timeline means "start again".
    if (!this.playing() && this.position() >= 1) {
      this.position.set(0);
      this.emit();
    }

    this.playing.update((playing) => !playing);
  }

  protected setSpeed(speed: Speed): void {
    this.speed.set(speed);
  }

  /** Steps one year, for precise navigation the scrubber cannot give. */
  protected step(years: number): void {
    const total = Math.max(1, this.years().length);

    this.playing.set(false);
    this.position.set(Math.min(1, Math.max(0, this.position() + years / total)));
    this.emit();
  }

  protected onScrub(event: Event): void {
    this.playing.set(false);
    this.position.set(Number((event.target as HTMLInputElement).value) / 1000);
    this.emit();
  }

  /** Jumps to a year, so a spike in the distribution is directly clickable. */
  protected jumpToYear(index: number): void {
    const total = Math.max(1, this.years().length);

    this.playing.set(false);
    this.position.set(Math.min(1, (index + 0.99) / total));
    this.emit();
  }

  protected showEverything(): void {
    this.playing.set(false);
    this.position.set(1);
    this.emit();
  }

  protected toggleComparable(): void {
    this.comparableOnly.update((only) => !only);
    this.magnitudeFloorChanged.emit(this.comparableOnly() ? Timeline.comparableMagnitude : null);
  }

  private emit(): void {
    this.instantChanged.emit(this.showingAll() ? null : this.currentMs());
  }
}
