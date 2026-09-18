import { Injectable, computed, signal } from '@angular/core';

/**
 * A named time window offered as one click.
 *
 * `months` is counted back from the present rather than expressed as a fixed span, because "this
 * year" is not twelve months and "this month" is not thirty days — a reader asking for either means
 * the calendar unit they are living in.
 */
export type TimePreset = 'all' | 'this-month' | 'this-year' | 'twelve-months' | 'five-years';

/** The filter as the map applies it. Every bound is nullable, and null means "no bound". */
export interface EarthquakeFilter {
  readonly fromMs: number | null;
  readonly toMs: number | null;
  readonly minMagnitude: number | null;
  readonly maxMagnitude: number | null;
  readonly minDepthKm: number | null;
  readonly maxDepthKm: number | null;

  /**
   * Whether events whose depth the agency assigned rather than measured are drawn.
   *
   * True by default. Excluding them by default would quietly drop 43% of the archive, and their
   * presence is one of the things this platform exists to disclose — but a reader studying depth
   * needs to be able to take them out, because a fixed 33 km is not a measurement of anything.
   */
  readonly includeAssignedDepth: boolean;
}

/** A lossless snapshot of the reader's normal archive filter configuration. */
export interface EarthquakeFilterState extends EarthquakeFilter {
  readonly preset: TimePreset;
}

/**
 * The reader's filter over the earthquake archive.
 *
 * <b>Why a store rather than component state.</b> Three surfaces read it — the map's GPU-side
 * filter, the count in the corner, and the panel that sets it — and the panel is created and
 * destroyed as the rail opens and closes. Component state would reset every time it was reopened,
 * which is the bug the timeline's magnitude floor already documents.
 *
 * <b>The magnitude floor lives here too</b>, although the timeline sets it. The timeline's "M6.0+"
 * control is a disclosure device — before about 1970 only larger events were detected, so only M6
 * and above may be compared between eras — but it writes the same field a reader would set by hand,
 * and two controls owning one field is how a filter starts disagreeing with the count beside it.
 */
@Injectable({ providedIn: 'root' })
export class EarthquakeFilterStore {
  /** The explicit historically comparable preset; it is never applied merely by choosing a hazard. */
  static readonly historicalComparableMagnitudeFloor = 6;

  private readonly _fromMs = signal<number | null>(null);
  private readonly _toMs = signal<number | null>(null);
  private readonly _minMagnitude = signal<number | null>(null);
  private readonly _maxMagnitude = signal<number | null>(null);
  private readonly _minDepthKm = signal<number | null>(null);
  private readonly _maxDepthKm = signal<number | null>(null);
  private readonly _includeAssignedDepth = signal(true);
  private readonly _preset = signal<TimePreset>('all');

  readonly fromMs = this._fromMs.asReadonly();
  readonly toMs = this._toMs.asReadonly();
  readonly minMagnitude = this._minMagnitude.asReadonly();
  readonly maxMagnitude = this._maxMagnitude.asReadonly();
  readonly minDepthKm = this._minDepthKm.asReadonly();
  readonly maxDepthKm = this._maxDepthKm.asReadonly();
  readonly includeAssignedDepth = this._includeAssignedDepth.asReadonly();

  /** Which preset produced the current window, or `all` when the window is unbounded or custom. */
  readonly preset = this._preset.asReadonly();

  readonly filter = computed<EarthquakeFilter>(() => ({
    fromMs: this._fromMs(),
    toMs: this._toMs(),
    minMagnitude: this._minMagnitude(),
    maxMagnitude: this._maxMagnitude(),
    minDepthKm: this._minDepthKm(),
    maxDepthKm: this._maxDepthKm(),
    includeAssignedDepth: this._includeAssignedDepth(),
  }));

  /**
   * Whether anything is being filtered out.
   *
   * Drives the "showing N of 27,241" wording. A filtered count presented without saying so is the
   * platform's own guardrail broken — a subset stated as a total.
   */
  readonly isFiltered = computed(
    () =>
      this._fromMs() !== null
      || this._toMs() !== null
      || this._minMagnitude() !== null
      || this._maxMagnitude() !== null
      || this._minDepthKm() !== null
      || this._maxDepthKm() !== null
      || !this._includeAssignedDepth(),
  );

  /**
   * Applies a named window.
   *
   * @param now Injected rather than read from the clock, so the boundary arithmetic is testable and
   *   so a long-lived tab cannot compute "this month" against the month it was opened in.
   */
  applyPreset(preset: TimePreset, now: Date = new Date()): void {
    this._preset.set(preset);

    if (preset === 'all') {
      this._fromMs.set(null);
      this._toMs.set(null);

      return;
    }

    this._fromMs.set(EarthquakeFilterStore.windowStart(preset, now));
    // Deliberately no upper bound: every preset is "since", and the reader is looking at a record
    // that ends at the present. An upper bound of "now" would also fight the timeline scrubber,
    // which owns the upper edge while it is being dragged.
    this._toMs.set(null);
  }

  /**
   * Start of a named window, in UTC.
   *
   * <b>UTC, matching the archive.</b> Every stored instant is UTC and so are the catalogue's own day
   * boundaries. Computing "this month" in Philippine local time would place an event recorded at
   * 1994-11-14 19:15 UTC — 03:15 on the 15th here — in a different month from the record it came
   * from, which is the same date-split the Moro Gulf story is partly about.
   */
  private static windowStart(preset: Exclude<TimePreset, 'all'>, now: Date): number {
    switch (preset) {
      case 'this-month':
        return Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1);
      case 'this-year':
        return Date.UTC(now.getUTCFullYear(), 0, 1);
      case 'twelve-months':
        return Date.UTC(
          now.getUTCFullYear() - 1,
          now.getUTCMonth(),
          now.getUTCDate(),
          now.getUTCHours(),
          now.getUTCMinutes(),
        );
      case 'five-years':
        return Date.UTC(
          now.getUTCFullYear() - 5,
          now.getUTCMonth(),
          now.getUTCDate(),
          now.getUTCHours(),
          now.getUTCMinutes(),
        );
    }
  }

  setMagnitudeRange(min: number | null, max: number | null): void {
    this._minMagnitude.set(min);
    this._maxMagnitude.set(max);
  }

  setDepthRange(min: number | null, max: number | null): void {
    this._minDepthKm.set(min);
    this._maxDepthKm.set(max);
  }

  setIncludeAssignedDepth(include: boolean): void {
    this._includeAssignedDepth.set(include);
  }

  /** Captures every reader-controlled archive bound, including the preset shown by the UI. */
  snapshot(): EarthquakeFilterState {
    return { ...this.filter(), preset: this._preset() };
  }

  /** Restores a previously captured archive configuration without inferring any field. */
  restore(state: EarthquakeFilterState): void {
    this._fromMs.set(state.fromMs);
    this._toMs.set(state.toMs);
    this._minMagnitude.set(state.minMagnitude);
    this._maxMagnitude.set(state.maxMagnitude);
    this._minDepthKm.set(state.minDepthKm);
    this._maxDepthKm.set(state.maxDepthKm);
    this._includeAssignedDepth.set(state.includeAssignedDepth);
    this._preset.set(state.preset);
  }

  /** Clears every bound. The archive as it stands, which is the honest default to return to. */
  clear(): void {
    this._fromMs.set(null);
    this._toMs.set(null);
    this._minMagnitude.set(null);
    this._maxMagnitude.set(null);
    this._minDepthKm.set(null);
    this._maxDepthKm.set(null);
    this._includeAssignedDepth.set(true);
    this._preset.set('all');
  }
}
