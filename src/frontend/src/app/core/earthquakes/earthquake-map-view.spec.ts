import { describe, expect, it } from 'vitest';

import { type EarthquakeFilter } from './earthquake-filter-store';
import { containmentMapClause, eventMatchesEarthquakeMapView } from './earthquake-map-view';
import { toEarthquakeGeoJson } from '../visual/earthquake-geojson';

const FILTER: EarthquakeFilter = {
  fromMs: null,
  toMs: null,
  minMagnitude: 6,
  maxMagnitude: null,
  minDepthKm: null,
  maxDepthKm: 70,
  includeAssignedDepth: false,
};

const EVENT = {
  id: 'contained-visible',
  epochMs: 100,
  magnitude: 6.5,
  depthKm: 30,
  depthMeasured: true,
};

describe('earthquake map containment composition', () => {
  it('intersects authoritative membership with existing earthquake filters', () => {
    expect(
      eventMatchesEarthquakeMapView(EVENT, FILTER, null, null, new Set([EVENT.id])),
    ).toBe(true);
    expect(
      eventMatchesEarthquakeMapView(EVENT, FILTER, null, null, new Set(['another-event'])),
    ).toBe(false);
    expect(
      eventMatchesEarthquakeMapView(
        { ...EVENT, magnitude: 5.9 },
        FILTER,
        null,
        null,
        new Set([EVENT.id]),
      ),
    ).toBe(false);
    expect(
      eventMatchesEarthquakeMapView(
        { ...EVENT, depthMeasured: false },
        FILTER,
        null,
        null,
        new Set([EVENT.id]),
      ),
    ).toBe(false);
  });

  it('restores ordinary archive membership when containment is cleared', () => {
    expect(containmentMapClause({ status: 'inactive' })).toBeNull();
    expect(eventMatchesEarthquakeMapView(EVENT, FILTER, null, null, null)).toBe(true);
  });

  it('hides the previous LGU set while a replacement request is loading', () => {
    expect(containmentMapClause({ status: 'loading', canonicalPsgcCode: '1606810000' })).toEqual([
      '==',
      ['get', 'id'],
      '__calametra_containment_loading__',
    ]);
  });

  it('keeps the existing event id used by the earthquake detail click flow', () => {
    const geojson = toEarthquakeGeoJson([
      { i: EVENT.id, y: 9, x: 126, m: 6.5, s: 'Mw', d: 30, q: 1, t: 100, n: false },
    ]);

    expect(geojson.features[0]?.properties.id).toBe(EVENT.id);
    expect(geojson.features[0]?.geometry.coordinates).toEqual([126, 9]);
  });
});
