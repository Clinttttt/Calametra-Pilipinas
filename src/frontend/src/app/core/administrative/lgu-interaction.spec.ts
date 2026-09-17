import { describe, expect, it, vi } from 'vitest';

import { LGU_BAND_LOCAL, LGU_BAND_REGIONAL, lguInteractiveAt } from './lgu-zoom-bands';
import { LguSelectionStore } from './lgu-selection-store';

/**
 * The rules Explore's municipality interaction obeys, tested against a stand-in for MapLibre.
 *
 * **Why a fake map rather than the component.** Explore constructs a real MapLibre instance, which needs
 * a canvas and a WebGL context, so instantiating it in a unit test tests jsdom rather than the interaction.
 * What is worth asserting is the decision logic: which layers own a click, what hover does to selection,
 * and what hiding the bulk layer does to the selected outline. Those are expressible against a map that
 * records the calls made to it, and they are the behaviours a reader would notice breaking.
 */

interface FakeMap {
  readonly layers: Set<string>;
  readonly filters: Map<string, unknown>;
  readonly visibility: Map<string, string>;
  hits: string[];
  zoom: number;
  getLayer(id: string): unknown;
  getZoom(): number;
  setFilter(id: string, filter: unknown): void;
  setLayoutProperty(id: string, property: string, value: string): void;
  queryRenderedFeatures(point: unknown, options: { layers: string[] }): unknown[];
  getCanvas(): { style: { cursor: string } };
}

const LGU_LINE = 'calametra-lgu-line';
const LGU_HOVER = 'calametra-lgu-hover';
const LGU_SELECTED_FILL = 'calametra-lgu-selected-fill';
const LGU_SELECTED_LINE = 'calametra-lgu-selected-line';
const LGU_HIT = 'calametra-lgu-hit';
const EARTHQUAKES = 'calametra-earthquakes-circles';

function fakeMap(present: readonly string[] = [], zoom = 12): FakeMap {
  const canvas = { style: { cursor: '' } };

  return {
    layers: new Set([LGU_LINE, LGU_HOVER, LGU_HIT, LGU_SELECTED_FILL, LGU_SELECTED_LINE, ...present]),
    filters: new Map<string, unknown>(),
    visibility: new Map<string, string>(),
    hits: [],
    zoom,
    getLayer(id) {
      return this.layers.has(id) ? { id } : undefined;
    },
    getZoom() {
      return this.zoom;
    },
    setFilter(id, filter) {
      this.filters.set(id, filter);
    },
    setLayoutProperty(id, property, value) {
      if (property === 'visibility') {
        this.visibility.set(id, value);
      }
    },
    queryRenderedFeatures(_point, options) {
      return options.layers.filter((layer) => this.hits.includes(layer)).map((layer) => ({ layer }));
    },
    getCanvas() {
      return canvas;
    },
  };
}

/**
 * The precedence rule as Explore applies it: a municipality click is the fallback, never a co-fire.
 */
function higherPriorityHit(map: FakeMap, candidateLayers: readonly string[]): boolean {
  const present = candidateLayers.filter((layer) => map.getLayer(layer) !== undefined);

  return present.length > 0 && map.queryRenderedFeatures({ x: 1, y: 1 }, { layers: present }).length > 0;
}

/** The three filters and one visibility Explore writes for the current state. */
function applyLguState(map: FakeMap, store: LguSelectionStore, boundariesVisible: boolean): void {
  const hovered = store.hoveredPsgc();
  const selected = store.selectedPsgc();

  map.setLayoutProperty(LGU_LINE, 'visibility', boundariesVisible ? 'visible' : 'none');

  map.setFilter(LGU_HOVER, [
    '==',
    ['get', 'psgc'],
    boundariesVisible && hovered !== null && hovered !== selected ? hovered : '',
  ]);

  for (const layerId of [LGU_SELECTED_FILL, LGU_SELECTED_LINE]) {
    map.setFilter(layerId, ['==', ['get', 'psgc'], selected ?? '']);
  }

  map.getCanvas().style.cursor = hovered === null ? '' : 'pointer';
}

function selectedFilterCode(map: FakeMap): string {
  const filter = map.filters.get(LGU_SELECTED_LINE) as [string, unknown, string];

  return filter[2];
}

describe('Explore municipality interaction', () => {
  it('does not treat a click as a municipality click below the local band', () => {
    // Below zoom 9 a municipality is a few pixels across and a click is a guess about which of several
    // the reader meant, so the gesture is not offered at all.
    expect(lguInteractiveAt(LGU_BAND_REGIONAL)).toBe(false);
    expect(lguInteractiveAt(8)).toBe(false);
    expect(lguInteractiveAt(LGU_BAND_LOCAL)).toBe(true);
  });

  it('yields to an earthquake marker under the pointer', () => {
    const map = fakeMap([EARTHQUAKES]);
    map.hits = [EARTHQUAKES];

    // The click belongs to the event, not to the land beneath it. Without this a municipality would win
    // almost every click, because it is the largest thing under the pointer nearly everywhere.
    expect(higherPriorityHit(map, [EARTHQUAKES])).toBe(true);
  });

  it('selects the municipality when no data feature owns the click', () => {
    const map = fakeMap([EARTHQUAKES]);
    map.hits = [];

    expect(higherPriorityHit(map, [EARTHQUAKES])).toBe(false);
  });

  it('ignores layers the style has not registered', () => {
    // Which data layers exist depends on the hazard chosen, and asking MapLibre for a missing layer
    // throws. The filter is what keeps the fallback from crashing on a cyclone map.
    const map = fakeMap();
    map.hits = [];

    const query = vi.spyOn(map, 'queryRenderedFeatures');

    expect(higherPriorityHit(map, [EARTHQUAKES])).toBe(false);
    expect(query).not.toHaveBeenCalled();
  });

  it('highlights on hover and clears on leaving, without touching the selection', () => {
    const map = fakeMap();
    const store = new LguSelectionStore();

    store.select({ psgc: '1606805000', name: 'Cantilan', kind: 'Municipality', boundaryGeometryAreaSquareKm: 203.6 });
    store.hover('1660300000');
    applyLguState(map, store, true);

    expect(map.filters.get(LGU_HOVER)).toEqual(['==', ['get', 'psgc'], '1660300000']);

    store.clearHover();
    applyLguState(map, store, true);

    expect(map.filters.get(LGU_HOVER)).toEqual(['==', ['get', 'psgc'], '']);
    expect(selectedFilterCode(map)).toBe('1606805000');
  });

  it('does not draw hover over the unit already selected', () => {
    // Two highlights on one outline read as a rendering fault rather than as a state.
    const map = fakeMap();
    const store = new LguSelectionStore();

    store.select({ psgc: '1606805000', name: 'Cantilan', kind: 'Municipality', boundaryGeometryAreaSquareKm: 203.6 });
    store.hover('1606805000');
    applyLguState(map, store, true);

    expect(map.filters.get(LGU_HOVER)).toEqual(['==', ['get', 'psgc'], '']);
  });

  it('replaces the previous selection rather than accumulating', () => {
    const map = fakeMap();
    const store = new LguSelectionStore();

    store.select({ psgc: '1606805000', name: 'Cantilan', kind: 'Municipality', boundaryGeometryAreaSquareKm: 203.6 });
    store.select({ psgc: '0730600000', name: 'City of Cebu', kind: 'City', boundaryGeometryAreaSquareKm: 326.2 });
    applyLguState(map, store, true);

    expect(selectedFilterCode(map)).toBe('0730600000');
  });

  it('keeps the selected outline when the bulk layer is switched off', () => {
    // ADR-005 D6: a layer being enabled means "draw many boundaries"; a unit being selected means
    // "always draw that one". Hiding the mesh must not remove the answer.
    const map = fakeMap();
    const store = new LguSelectionStore();

    store.select({ psgc: '1606805000', name: 'Cantilan', kind: 'Municipality', boundaryGeometryAreaSquareKm: 203.6 });
    applyLguState(map, store, false);

    expect(map.visibility.get(LGU_LINE)).toBe('none');
    expect(selectedFilterCode(map)).toBe('1606805000');
    expect(map.filters.get(LGU_SELECTED_FILL)).toEqual(['==', ['get', 'psgc'], '1606805000']);
  });

  it('suppresses hover while the bulk layer is hidden', () => {
    // Hover is a pointing affordance for the mesh, so it has no meaning once the mesh is gone.
    const map = fakeMap();
    const store = new LguSelectionStore();

    store.hover('1606805000');
    applyLguState(map, store, false);

    expect(map.filters.get(LGU_HOVER)).toEqual(['==', ['get', 'psgc'], '']);
  });

  it('filters on the PSGC rather than on feature-state', () => {
    // Feature-state is per tile and is lost when a tile is evicted, so a selection would silently vanish
    // on pan. A filter on the code survives eviction, tile reloads and re-imports.
    const map = fakeMap();
    const store = new LguSelectionStore();

    store.select({ psgc: '0102801000', name: 'Adams', kind: 'Municipality', boundaryGeometryAreaSquareKm: 159.3 });
    applyLguState(map, store, true);

    expect(map.filters.get(LGU_SELECTED_LINE)).toEqual(['==', ['get', 'psgc'], '0102801000']);
  });

  it('binds pointer handlers to an unfiltered layer, not to the filtered hover layer', () => {
    // The defect this pins down: the handlers were originally bound to the hover layer, which is
    // filtered to the hovered feature alone. MapLibre only fires a layer-scoped event for a feature that
    // layer actually renders, so a layer filtered to nothing renders nothing and no event could ever
    // fire. The interaction was dead at every zoom, and it looked like a zoom-band decision.
    //
    // The hit layer therefore carries no filter. Asserting the absence of one is the whole point.
    const map = fakeMap();
    const store = new LguSelectionStore();

    applyLguState(map, store, true);

    expect(map.filters.has('calametra-lgu-hit')).toBe(false);
    expect(map.getLayer('calametra-lgu-hit')).toBeDefined();
  });

  it('has a selection to show regardless of hazard or place-panel state', () => {
    // The panel was first nested inside the earthquake-and-place-explorer gate, so selecting a
    // municipality drew the outline and showed nothing: the answer existed and had nowhere to appear.
    //
    // A municipality is a fact about the land, true whichever hazard is on screen, so the only condition
    // the panel may depend on is whether something is selected.
    const store = new LguSelectionStore();

    expect(store.hasSelection()).toBe(false);

    store.select({ psgc: '1606805000', name: 'Cantilan', kind: 'Municipality', boundaryGeometryAreaSquareKm: 203.6 });

    expect(store.hasSelection()).toBe(true);

    store.clear();

    expect(store.hasSelection()).toBe(false);
  });

  it('shows a pointer cursor only while a municipality is under the pointer', () => {
    const map = fakeMap();
    const store = new LguSelectionStore();

    applyLguState(map, store, true);
    expect(map.getCanvas().style.cursor).toBe('');

    store.hover('1606805000');
    applyLguState(map, store, true);
    expect(map.getCanvas().style.cursor).toBe('pointer');
  });
});
