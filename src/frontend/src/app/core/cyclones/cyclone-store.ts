import { Injectable, computed, inject, signal } from '@angular/core';

import { CalametraApi } from '../api/calametra-api';
import type { AgencyTrack, CycloneOrder, CycloneSummary, CycloneTrack } from '../api/contracts';
import { NotificationStore } from '../notifications/notification-store';
import { mostFullyReported } from './agency-ranking';

/**
 * Owns which storm is open, which agency's reading of it is emphasised, and where playback
 * has reached.
 *
 * The emphasised agency is a first-class piece of state rather than a rendering detail. Four
 * tracks drawn with equal weight are unreadable, and colour cannot separate them — hue is the
 * depth channel on this map. So one agency is prominent at a time and the rest stay faint,
 * which turns comparison into a deliberate act rather than a visual puzzle.
 */
@Injectable({ providedIn: 'root' })
export class CycloneStore {
  private readonly api = inject(CalametraApi);
  private readonly notifications = inject(NotificationStore);

  private readonly _open = signal(false);
  private readonly _season = signal<number | null>(null);

  /**
   * List ordering.
   *
   * Intensity by default. Recency surfaces provisional single-agency tracks first, which are the
   * least informative records the archive holds — no cross-agency comparison and no local name.
   */
  private readonly _order = signal<CycloneOrder>('Intensity');

  /** The current name filter. Empty string means unfiltered. */
  private readonly _query = signal('');

  /** Pending debounce timer for the name filter, or null when none is scheduled. */
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  /**
   * Sequence number for list requests.
   *
   * Guards against an out-of-order response overwriting a newer one. Typing "SURI" issues a
   * request for "SUR" and then for "SURI", and nothing guarantees the first returns first — without
   * this the list could settle on the results of a query the reader has already moved past.
   */
  private listRequest = 0;
  private readonly _storms = signal<readonly CycloneSummary[]>([]);
  private readonly _loadingStorms = signal(false);

  private readonly _track = signal<CycloneTrack | null>(null);
  private readonly _loadingTrack = signal(false);

  private readonly _emphasisedSlug = signal<string | null>(null);

  /**
   * Playback position as an index into the emphasised agency's fixes.
   *
   * Indexed rather than held as a timestamp because agencies do not report on the same
   * schedule — JTWC publishes three-hourly fixes where JMA publishes six-hourly, so a shared
   * clock would leave one track stepping and the other still.
   */
  private readonly _fixIndex = signal(0);
  private readonly _playing = signal(false);

  private timer?: ReturnType<typeof setInterval>;

  readonly open = this._open.asReadonly();
  readonly season = this._season.asReadonly();
  readonly order = this._order.asReadonly();
  readonly query = this._query.asReadonly();
  readonly storms = this._storms.asReadonly();
  readonly loadingStorms = this._loadingStorms.asReadonly();
  readonly track = this._track.asReadonly();
  readonly loadingTrack = this._loadingTrack.asReadonly();
  readonly emphasisedSlug = this._emphasisedSlug.asReadonly();
  readonly fixIndex = this._fixIndex.asReadonly();
  readonly playing = this._playing.asReadonly();

  /** The agency track currently emphasised, or the most fully reported one as a default. */
  readonly emphasised = computed<AgencyTrack | null>(() => {
    const track = this._track();

    if (track === null || track.tracks.length === 0) {
      return null;
    }

    const slug = this._emphasisedSlug();
    const chosen = track.tracks.find((candidate) => candidate.sourceSlug === slug);

    return chosen ?? mostFullyReported(track.tracks);
  });

  /** The fix playback is currently at, on the emphasised track. */
  readonly currentFix = computed(() => {
    const fixes = this.emphasised()?.fixes ?? [];

    return fixes.length === 0 ? null : (fixes[this._fixIndex()] ?? fixes[0]);
  });

  readonly fixCount = computed(() => this.emphasised()?.fixes.length ?? 0);

  /** Seasons offered in the picker, newest first. */
  readonly seasons = computed(() => {
    const now = new Date().getUTCFullYear();

    return Array.from({ length: now - 2009 }, (_, index) => now - index);
  });

  openPanel(): void {
    if (this._open()) {
      return;
    }

    this._open.set(true);

    if (this._storms().length === 0) {
      this.loadStorms();
    }
  }

  closePanel(): void {
    this._open.set(false);
    this.clearTrack();
  }

  setOrder(order: CycloneOrder): void {
    if (order === this._order()) {
      return;
    }

    this._order.set(order);
    this.loadStorms();
  }

  setSeason(season: number | null): void {
    if (season === this._season()) {
      return;
    }

    this._season.set(season);
    this.clearTrack();
    this.loadStorms();
  }

  /**
   * Filters the list by name, matching the international or PAGASA name server-side.
   *
   * Debounced rather than fired per keystroke: "SURIGAE" is seven requests otherwise, six of them
   * already stale by the time they return. 250 ms is below the point where the list feels
   * unresponsive and above a normal inter-key interval.
   */
  setQuery(value: string): void {
    const next = value.trim();

    if (next === this._query()) {
      return;
    }

    this._query.set(next);
    this.clearTrack();

    if (this.searchTimer !== null) {
      clearTimeout(this.searchTimer);
    }

    this.searchTimer = setTimeout(() => {
      this.searchTimer = null;
      this.loadStorms();
    }, 250);
  }

  selectStorm(stormId: string): void {
    if (this._track()?.id === stormId) {
      return;
    }

    this.stop();
    this._track.set(null);
    this._emphasisedSlug.set(null);
    this._fixIndex.set(0);
    this._loadingTrack.set(true);

    this.api.getCycloneTrack(stormId).subscribe({
      next: (track) => {
        this._track.set(track);
        this._loadingTrack.set(false);
      },
      error: () => {
        this._loadingTrack.set(false);
        this.notifications.error('The cyclone track could not be loaded.');
      },
    });
  }

  /** Emphasises one agency's reading, resetting playback to its own first fix. */
  emphasise(sourceSlug: string): void {
    if (sourceSlug === this._emphasisedSlug()) {
      return;
    }

    this.stop();
    this._emphasisedSlug.set(sourceSlug);

    // Reset rather than clamp: fix indices are per-agency, so keeping the position would
    // land the reader at an unrelated moment on the new track.
    this._fixIndex.set(0);
  }

  clearTrack(): void {
    this.stop();
    this._track.set(null);
    this._emphasisedSlug.set(null);
    this._fixIndex.set(0);
  }

  setFixIndex(index: number): void {
    const count = this.fixCount();

    if (count === 0) {
      return;
    }

    this._fixIndex.set(Math.min(Math.max(index, 0), count - 1));
  }

  step(delta: number): void {
    this.stop();
    this.setFixIndex(this._fixIndex() + delta);
  }

  togglePlayback(): void {
    if (this._playing()) {
      this.stop();
      return;
    }

    if (this.fixCount() === 0) {
      return;
    }

    // Restart from the beginning when already at the end, so pressing play at the end of a
    // track replays it rather than doing nothing.
    if (this._fixIndex() >= this.fixCount() - 1) {
      this._fixIndex.set(0);
    }

    this._playing.set(true);

    // 450 ms per fix. Fixes are three to six hours apart, so this is roughly a day of storm
    // life every two seconds — fast enough to read as motion, slow enough to follow.
    this.timer = setInterval(() => {
      const next = this._fixIndex() + 1;

      if (next >= this.fixCount()) {
        this.stop();
        return;
      }

      this._fixIndex.set(next);
    }, 450);
  }

  stop(): void {
    if (this.timer !== undefined) {
      clearInterval(this.timer);
      this.timer = undefined;
    }

    this._playing.set(false);
  }

  private loadStorms(): void {
    this._loadingStorms.set(true);

    const token = ++this.listRequest;

    this.api
      .searchCyclones(
        this._season() ?? undefined,
        false,
        this._order(),
        this._query() === '' ? undefined : this._query(),
      )
      .subscribe({
        next: (storms) => {
          // Discard a response that a later request has already superseded.
          if (token !== this.listRequest) {
            return;
          }

          this._storms.set(storms);
          this._loadingStorms.set(false);
        },
        error: () => {
          if (token !== this.listRequest) {
            return;
          }

          this._loadingStorms.set(false);
          this.notifications.error('Cyclone records could not be loaded.');
        },
      });
  }
}
