import { Injectable, computed, inject, signal } from '@angular/core';

import { CalametraApi } from '../api/calametra-api';
import type { EarthquakeComparison } from '../api/contracts';
import { NotificationStore } from '../notifications/notification-store';

/**
 * Owns which pair of earthquakes is being compared.
 *
 * The left side is the event already in view and the right is the one brought in, so the
 * ordering is meaningful and the deltas read in one direction. Swapping is offered rather
 * than inferred, because which event is the subject of a comparison is the reader's choice.
 */
@Injectable({ providedIn: 'root' })
export class ComparisonStore {
  private readonly api = inject(CalametraApi);
  private readonly notifications = inject(NotificationStore);

  private readonly _result = signal<EarthquakeComparison | null>(null);
  private readonly _loading = signal(false);
  private readonly _pair = signal<{ left: string; right: string } | null>(null);

  readonly result = this._result.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly pair = this._pair.asReadonly();

  /** True while a comparison is open, whether or not the data has arrived. */
  readonly active = computed(() => this._pair() !== null);

  /**
   * The two epicentres, for drawing on the map.
   *
   * Derived rather than stored separately so it cannot drift from the loaded comparison.
   * Null until the data arrives, which is what keeps the map from drawing a stale pair.
   */
  readonly endpoints = computed(() => {
    const result = this._result();

    if (result === null) {
      return null;
    }

    return {
      left: { latitude: result.left.latitude, longitude: result.left.longitude },
      right: { latitude: result.right.latitude, longitude: result.right.longitude },
    };
  });

  compare(leftEventId: string, rightEventId: string): void {
    if (leftEventId === rightEventId) {
      return;
    }

    this._pair.set({ left: leftEventId, right: rightEventId });
    this.load();
  }

  /** Reverses which event is the subject, so the deltas read the other way. */
  swap(): void {
    const pair = this._pair();

    if (pair === null) {
      return;
    }

    this._pair.set({ left: pair.right, right: pair.left });
    this.load();
  }

  close(): void {
    this._pair.set(null);
    this._result.set(null);
    this._loading.set(false);
  }

  private load(): void {
    const pair = this._pair();

    if (pair === null) {
      return;
    }

    this._loading.set(true);

    this.api.compareEarthquakes(pair.left, pair.right).subscribe({
      next: (result) => {
        this._result.set(result);
        this._loading.set(false);
      },
      error: () => {
        this._loading.set(false);
        this.notifications.error('The comparison could not be loaded.');
        this.close();
      },
    });
  }
}
