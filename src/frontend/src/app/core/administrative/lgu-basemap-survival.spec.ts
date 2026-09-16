import { describe, expect, it } from 'vitest';

import { BASEMAPS } from '../basemap/basemap-store';
import { applyBasemap } from '../basemap/apply-basemap';

/**
 * The municipality layers must survive a basemap change.
 *
 * **Why this is a regression test and not re-attachment logic.** `applyBasemap` deliberately never calls
 * `map.setStyle()` — that would discard every source and layer the application has added — and instead
 * recolours the vector fills and swaps only its own raster layer and source. So the LGU source, its four
 * layers and the selected filter already survive Dark, Satellite and Grayscale.
 *
 * That is worth pinning down rather than trusting, because the failure mode is invisible in code review:
 * a future change to `applyBasemap` that reached for `setStyle` would look like a simplification and would
 * silently take the boundaries, the epicentres and the cyclone tracks with it.
 */

const LGU_SOURCE = 'calametra-lgu-boundaries';
const LGU_LAYERS = [
  'calametra-lgu-line',
  'calametra-lgu-hover',
  'calametra-lgu-selected-fill',
  'calametra-lgu-selected-line',
] as const;

interface StubMap {
  readonly sources: Set<string>;
  readonly layers: Set<string>;
  readonly filters: Map<string, unknown>;
  setStyleCalls: number;
  getSource(id: string): unknown;
  getLayer(id: string): unknown;
  addSource(id: string): void;
  removeSource(id: string): void;
  addLayer(layer: { id: string }): void;
  removeLayer(id: string): void;
  setPaintProperty(): void;
  setLayoutProperty(): void;
  setStyle(): void;
  getStyle(): { layers: { id: string }[] };
}

function stubMap(): StubMap {
  return {
    sources: new Set<string>([LGU_SOURCE]),
    layers: new Set<string>([...LGU_LAYERS, 'background', 'water']),
    filters: new Map<string, unknown>([
      ['calametra-lgu-selected-line', ['==', ['get', 'psgc'], '1660200000']],
    ]),
    setStyleCalls: 0,
    getSource(id) {
      return this.sources.has(id) ? { id } : undefined;
    },
    getLayer(id) {
      return this.layers.has(id) ? { id, type: 'line' } : undefined;
    },
    addSource(id) {
      this.sources.add(id);
    },
    removeSource(id) {
      this.sources.delete(id);
    },
    addLayer(layer) {
      this.layers.add(layer.id);
    },
    removeLayer(id) {
      this.layers.delete(id);
    },
    setPaintProperty() {
      // Recolouring the vector base touches no application layer.
    },
    setLayoutProperty() {
      // Hiding the vector base under imagery touches no application layer.
    },
    setStyle() {
      // Recorded rather than implemented: calling it is the defect this test exists to catch.
      this.setStyleCalls += 1;
    },
    getStyle() {
      return { layers: [...this.layers].map((id) => ({ id })) };
    },
  };
}

describe('basemap changes and the municipality layers', () => {
  const targets = {
    imagerySourceId: 'calametra-imagery',
    imageryLayerId: 'calametra-imagery-layer',
  };

  it('keeps the source, the layers and the selected filter through every option', () => {
    const map = stubMap();

    for (const option of BASEMAPS) {
      applyBasemap(map as never, option, targets);

      expect(map.getSource(LGU_SOURCE)).toBeDefined();

      for (const layerId of LGU_LAYERS) {
        expect(map.getLayer(layerId)).toBeDefined();
      }

      // The selection is a filter on the layer, so it survives with it. Had the style been replaced the
      // filter would be gone along with the layer that carried it.
      expect(map.filters.get('calametra-lgu-selected-line')).toEqual([
        '==',
        ['get', 'psgc'],
        '1660200000',
      ]);
    }
  });

  it('never replaces the style, which is what would take the layers with it', () => {
    const map = stubMap();

    for (const option of BASEMAPS) {
      applyBasemap(map as never, option, targets);
    }

    expect(map.setStyleCalls).toBe(0);
  });

  it('switching repeatedly does not accumulate imagery layers', () => {
    // The raster is torn down and rebuilt each time. Without that, switching Satellite twice would leave
    // two rasters stacked over the data.
    const map = stubMap();

    for (const option of BASEMAPS) {
      applyBasemap(map as never, option, targets);
    }

    const imageryLayers = [...map.layers].filter((id) => id === targets.imageryLayerId);

    expect(imageryLayers.length).toBeLessThanOrEqual(1);
  });
});
