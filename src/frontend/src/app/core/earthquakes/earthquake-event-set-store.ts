import { Injectable, computed, signal } from '@angular/core';

/**
 * Which earthquake population the reader has deliberately put on the map.
 *
 * This is separate from hazard selection: Earthquakes can be the active subject while no event
 * population is shown. It is deliberately earthquake-specific because the available populations
 * and their scientific caveats do not generalise to other hazards.
 */
export type EarthquakeEventSet =
  | 'none'
  | 'historicalComparable'
  | 'customFiltered'
  | 'completeCatalogue'
  | 'isolatedEvent'
  | 'lguFocus';

@Injectable({ providedIn: 'root' })
export class EarthquakeEventSetStore {
  private readonly eventSetState = signal<EarthquakeEventSet>('none');

  readonly eventSet = this.eventSetState.asReadonly();
  readonly hasDisplayedPopulation = computed(() => this.eventSetState() !== 'none');

  select(eventSet: EarthquakeEventSet): void {
    this.eventSetState.set(eventSet);
  }

  /** A submitted empty filter is an explicit complete-catalogue choice, not a return to idle. */
  selectFilterResult(hasMeaningfulFilter: boolean): void {
    this.eventSetState.set(hasMeaningfulFilter ? 'customFiltered' : 'completeCatalogue');
  }
}
