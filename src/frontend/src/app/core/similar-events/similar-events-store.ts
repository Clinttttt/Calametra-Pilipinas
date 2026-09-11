import { Injectable, computed, inject, signal } from '@angular/core';

import { CalametraApi } from '../api/calametra-api';
import type { SimilarEarthquakes } from '../api/contracts';
import { NotificationStore } from '../notifications/notification-store';

/**
 * Owns which event's similar matches are shown, and how strictly they are matched.
 *
 * Follows the same split as the other stores: this holds intent, the component renders
 * consequence. Loading is explicit rather than automatic on selection — the search is a
 * secondary question about an event, so it should not fire every time one is clicked.
 */
@Injectable({ providedIn: 'root' })
export class SimilarEventsStore {
  private readonly api = inject(CalametraApi);
  private readonly notifications = inject(NotificationStore);

  private readonly _eventId = signal<string | null>(null);
  private readonly _result = signal<SimilarEarthquakes | null>(null);
  private readonly _loading = signal(false);
  private readonly _failed = signal(false);

  /**
   * Search radius in kilometres.
   *
   * 150 km default. The server caps this at 300: past roughly 60% table selectivity
   * PostGIS abandons the spatial index and the query time jumps from milliseconds to tens
   * of seconds, so the slider must not offer values that would hit that.
   */
  private readonly _maxDistanceKm = signal(150);

  /**
   * Whether candidates must report a comparable magnitude scale.
   *
   * On by default. Relaxing it does not make the readings comparable — it admits them with
   * the magnitude term omitted from their score, ranked on distance and depth alone. The
   * UI has to say so, which is what `admittedWithIncomparableMagnitude` is for.
   */
  private readonly _requireComparableScale = signal(true);

  /** Whether candidates must carry a measured rather than assigned depth. */
  private readonly _requireMeasuredDepth = signal(true);

  readonly eventId = this._eventId.asReadonly();
  readonly result = this._result.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly failed = this._failed.asReadonly();
  readonly maxDistanceKm = this._maxDistanceKm.asReadonly();
  readonly requireComparableScale = this._requireComparableScale.asReadonly();
  readonly requireMeasuredDepth = this._requireMeasuredDepth.asReadonly();

  /** True once a search has been run for the current event. */
  readonly hasRun = computed(() => this._result() !== null || this._loading() || this._failed());

  /**
   * How many nearby events the filters set aside, as a single figure for the caption.
   *
   * Deliberately not a sum of the two exclusion counts — they overlap, since one event can
   * both use an incomparable scale and carry an assigned depth. This is the honest
   * arithmetic: everything nearby that was not scored.
   */
  readonly setAside = computed(() => {
    const result = this._result();

    return result === null ? 0 : result.nearbyEvents - result.candidatesConsidered;
  });

  /** Points the search at an event, discarding any previous result. */
  open(eventId: string): void {
    if (this._eventId() === eventId) {
      return;
    }

    this._eventId.set(eventId);
    this._result.set(null);
    this._failed.set(false);
  }

  close(): void {
    this._eventId.set(null);
    this._result.set(null);
    this._failed.set(false);
  }

  setMaxDistanceKm(km: number): void {
    if (km === this._maxDistanceKm()) {
      return;
    }

    this._maxDistanceKm.set(km);
    this.reloadIfRun();
  }

  setRequireComparableScale(require: boolean): void {
    if (require === this._requireComparableScale()) {
      return;
    }

    this._requireComparableScale.set(require);
    this.reloadIfRun();
  }

  setRequireMeasuredDepth(require: boolean): void {
    if (require === this._requireMeasuredDepth()) {
      return;
    }

    this._requireMeasuredDepth.set(require);
    this.reloadIfRun();
  }

  search(): void {
    const eventId = this._eventId();

    if (eventId === null || this._loading()) {
      return;
    }

    this._loading.set(true);
    this._failed.set(false);

    this.api
      .getSimilarEarthquakes(eventId, {
        maxDistanceKm: this._maxDistanceKm(),
        requireComparableMagnitudeScale: this._requireComparableScale(),
        excludeOperatorAssignedDepths: this._requireMeasuredDepth(),
        limit: 10,
      })
      .subscribe({
        next: (result) => {
          this._result.set(result);
          this._loading.set(false);
        },
        error: () => {
          this._loading.set(false);
          this._failed.set(true);
          this.notifications.error('Similar events could not be loaded.');
        },
      });
  }

  /** Re-runs only when a search has already been shown, so changing a filter first is free. */
  private reloadIfRun(): void {
    if (this._result() !== null) {
      this.search();
    }
  }
}
