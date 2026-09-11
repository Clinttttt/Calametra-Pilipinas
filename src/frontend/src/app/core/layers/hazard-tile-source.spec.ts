import { describe, expect, it } from 'vitest';

import { hazardRasterSource } from './hazard-tile-source';
import type { HazardLayer } from '../api/contracts';

/**
 * A catalogue entry with only the fields this decision reads.
 *
 * Built from a partial rather than a full fixture so a test states what it depends on. If the
 * function starts reading another field, these tests stop compiling — which is the point.
 */
function layer(overrides: Partial<HazardLayer>): HazardLayer {
  return {
    id: 'layer-1',
    hazardType: 'RainInducedLandslide',
    lens: 'Cyclone',
    displayName: 'Rain-induced Landslide Susceptibility',
    deliveryMode: 'RemoteWms',
    supportsFeatureInfo: true,
    supportsCachedTiles: false,
    isEnabledByDefault: false,
    sortOrder: 30,
    explainer: null,
    interpretationNote: null,
    sourceAgency: 'DOST-MGB',
    sourceDatasetName: 'Detailed geohazard susceptibility maps',
    attribution: 'Geohazard susceptibility mapping © DOST-MGB.',
    sourceUrl: null,
    termsUrl: null,
    ...overrides,
  } as HazardLayer;
}

describe('hazardRasterSource', () => {
  it('addresses a cached layer on the tile grid', () => {
    const source = hazardRasterSource(
      layer({ supportsCachedTiles: true }),
      'http://localhost:5130',
    );

    expect(source.tiles[0]).toBe('http://localhost:5130/api/hazard-layers/layer-1/tile/{z}/{x}/{y}');
  });

  it('asks the publisher to render when there is no cache', () => {
    // PHIVOLCS publishes no tile cache for its hazard services, so this path has to stay.
    const source = hazardRasterSource(
      layer({ supportsCachedTiles: false, sourceAgency: 'DOST-PHIVOLCS' }),
      'http://localhost:5130',
    );

    expect(source.tiles[0]).toContain('bbox={bbox-epsg-3857}');
    expect(source.tiles[0]).not.toContain('{z}');
  });

  it('declares a tile size that matches the template', () => {
    // The trap this pins: MapLibre uses tileSize to decide which tiles to request, so a size that
    // disagrees with the template makes it ask for one tile where the cache holds four. The proxy
    // then returns a quarter of the ground stretched over the whole tile, which reads as a
    // projection fault rather than a configuration one.
    const cached = hazardRasterSource(layer({ supportsCachedTiles: true }), 'http://x');
    const rendered = hazardRasterSource(layer({ supportsCachedTiles: false }), 'http://x');

    expect(cached.tileSize).toBe(256);

    expect(rendered.tileSize).toBe(512);
    expect(rendered.tiles[0]).toContain('width=512');
    expect(rendered.tiles[0]).toContain('height=512');
  });

  it('places each placeholder exactly once', () => {
    // A duplicated placeholder yields tiles from one axis substituted into both, which produces a
    // diagonal band of imagery rather than an error.
    const tiles = hazardRasterSource(layer({ supportsCachedTiles: true }), 'http://x').tiles[0];

    for (const placeholder of ['{z}', '{x}', '{y}']) {
      expect(tiles.split(placeholder).length - 1).toBe(1);
    }
  });

  it('does not double the slash when the configured base URL ends in one', () => {
    // The base URL is runtime configuration so one build can be pointed at another API. Most
    // servers tolerate `//api/...`; some proxies do not.
    const source = hazardRasterSource(
      layer({ supportsCachedTiles: true }),
      'https://api.example.org/',
    );

    expect(source.tiles[0]).toBe(
      'https://api.example.org/api/hazard-layers/layer-1/tile/{z}/{x}/{y}',
    );
  });
});
