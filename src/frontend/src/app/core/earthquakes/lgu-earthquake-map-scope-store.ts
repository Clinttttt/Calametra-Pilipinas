import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, effect, inject, signal } from '@angular/core';

import { LguSelectionStore } from '../administrative/lgu-selection-store';
import { CalametraApi } from '../api/calametra-api';
import { type LguContainedEarthquakeMapData, type ProblemDetails } from '../api/contracts';
import { HazardModeStore } from '../hazards/hazard-mode-store';
import { EarthquakeFilterStore, type EarthquakeFilterState } from './earthquake-filter-store';

export type LguEarthquakeMapScopeState =
  | { readonly status: 'inactive' }
  | { readonly status: 'loading'; readonly canonicalPsgcCode: string }
  | { readonly status: 'ready'; readonly data: LguContainedEarthquakeMapData }
  | { readonly status: 'unavailable'; readonly canonicalPsgcCode: string }
  | { readonly status: 'failed'; readonly canonicalPsgcCode: string };

/**
 * The optional administrative-containment scope over the existing earthquake map.
 *
 * Membership is supplied by PostGIS as canonical event ids. This store never tests points against tile
 * geometry, never mutates the place/radius workflow, and owns no MapLibre objects. Focus temporarily
 * starts from the complete authoritative set, while the reader's normal archive filters are restored on exit.
 */
@Injectable({ providedIn: 'root' })
export class LguEarthquakeMapScopeStore {
  private readonly api = inject(CalametraApi);
  private readonly lguSelection = inject(LguSelectionStore);
  private readonly hazardMode = inject(HazardModeStore);
  private readonly filters = inject(EarthquakeFilterStore);
  private readonly enabledState = signal(false);
  private readonly scopeState = signal<LguEarthquakeMapScopeState>({ status: 'inactive' });
  private readonly shownCountState = signal<number | null>(null);
  private normalFilterState: EarthquakeFilterState | null = null;
  private requestedCanonicalPsgcCode: string | null = null;

  readonly enabled = this.enabledState.asReadonly();
  readonly state = this.scopeState.asReadonly();
  readonly shownCount = this.shownCountState.asReadonly();
  readonly activeCanonicalPsgcCode = computed(() => {
    const state = this.scopeState();
    return state.status === 'ready'
      ? state.data.canonicalPsgcCode
      : state.status === 'inactive'
        ? null
        : state.canonicalPsgcCode;
  });

  constructor() {
    effect((onCleanup) => {
      const enabled = this.enabledState();
      const selected = this.lguSelection.selected();

      this.shownCountState.set(null);

      if (!enabled) {
        this.requestedCanonicalPsgcCode = null;
        this.scopeState.set({ status: 'inactive' });
        return;
      }

      if (selected === null) {
        this.restoreNormalFilters();
        this.enabledState.set(false);
        this.scopeState.set({ status: 'inactive' });
        return;
      }

      const canonicalPsgcCode = selected.psgc;
      if (this.requestedCanonicalPsgcCode !== canonicalPsgcCode) {
        // A new administrative subject starts as the complete contained set. Any filters the reader
        // experimented with inside the previous focus remain temporary and do not follow to this LGU.
        this.filters.clear();
        this.requestedCanonicalPsgcCode = canonicalPsgcCode;
      }
      this.scopeState.set({ status: 'loading', canonicalPsgcCode });

      const subscription = this.api.getLguContainedEarthquakeMapData(canonicalPsgcCode).subscribe({
        next: (data) => {
          if (this.enabledState() && this.lguSelection.selectedPsgc() === canonicalPsgcCode) {
            this.scopeState.set({ status: 'ready', data });
          }
        },
        error: (error: unknown) => {
          if (!this.enabledState() || this.lguSelection.selectedPsgc() !== canonicalPsgcCode) {
            return;
          }

          this.scopeState.set({
            status: isBoundaryUnavailable(error) ? 'unavailable' : 'failed',
            canonicalPsgcCode,
          });
        },
      });

      onCleanup(() => subscription.unsubscribe());
    });
  }

  activate(): void {
    if (this.lguSelection.selected() !== null && !this.enabledState()) {
      this.normalFilterState = this.filters.snapshot();
      this.filters.clear();
      this.hazardMode.select('earthquakes');
      this.enabledState.set(true);
    }
  }

  clear(): void {
    this.restoreNormalFilters();
    this.enabledState.set(false);
    this.scopeState.set({ status: 'inactive' });
    this.shownCountState.set(null);
  }

  /** Updated by Explore after applying the same client-side filters used by the map layers. */
  setShownCount(count: number | null): void {
    this.shownCountState.set(count);
  }

  private restoreNormalFilters(): void {
    if (this.normalFilterState !== null) {
      this.filters.restore(this.normalFilterState);
      this.normalFilterState = null;
    }
  }
}

function isBoundaryUnavailable(error: unknown): boolean {
  return (
    error instanceof HttpErrorResponse &&
    error.status === 404 &&
    (error.error as ProblemDetails | null)?.code === 'lgu_boundary.not_available'
  );
}
