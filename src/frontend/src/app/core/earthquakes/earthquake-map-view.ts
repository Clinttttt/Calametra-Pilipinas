import { type EarthquakeFilter } from './earthquake-filter-store';
import { type LguEarthquakeMapScopeState } from './lgu-earthquake-map-scope-store';

export interface LoadedEarthquakeMapEvent {
  readonly id: string;
  readonly epochMs: number;
  readonly magnitude: number | null;
  readonly depthKm: number | null;
  readonly depthMeasured: boolean;
}

/** MapLibre clause for server-authoritative LGU membership; null means ordinary archive view. */
export function containmentMapClause(state: LguEarthquakeMapScopeState): unknown[] | null {
  if (state.status === 'ready') {
    return ['in', ['get', 'id'], ['literal', state.data.points.map((point) => point.i)]];
  }

  return state.status === 'loading'
    ? ['==', ['get', 'id'], '__calametra_containment_loading__']
    : null;
}

/** Mirrors the map layer predicate for a viewport-independent visible count. */
export function eventMatchesEarthquakeMapView(
  event: LoadedEarthquakeMapEvent,
  filter: EarthquakeFilter,
  instantMs: number | null,
  isolatedEventId: string | null,
  containedIds: ReadonlySet<string> | null,
): boolean {
  return (containedIds === null || containedIds.has(event.id))
    && (isolatedEventId === null || event.id === isolatedEventId)
    && (instantMs === null || event.epochMs <= instantMs)
    && (filter.fromMs === null || event.epochMs >= filter.fromMs)
    && (filter.toMs === null || event.epochMs <= filter.toMs)
    && (filter.minMagnitude === null || (event.magnitude ?? -1) >= filter.minMagnitude)
    && (filter.maxMagnitude === null || (event.magnitude ?? 99) <= filter.maxMagnitude)
    && (filter.minDepthKm === null || (event.depthKm ?? -1) >= filter.minDepthKm)
    && (filter.maxDepthKm === null || (event.depthKm ?? 9999) <= filter.maxDepthKm)
    && (filter.includeAssignedDepth || event.depthMeasured);
}
