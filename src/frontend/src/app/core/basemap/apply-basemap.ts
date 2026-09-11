import type { Map as MapLibreMap } from 'maplibre-gl';

import type { BasemapOption } from './basemap-store';

/**
 * APPLYING A BASE LAYER TO A MAP
 *
 * Extracted from the Explore map so Story mode can use the same implementation. The two were
 * previously different: Explore honoured the reader's choice while the story map was hard-wired to
 * the dark vector style, so selecting Satellite changed one map and not the other. A reader who
 * picked imagery and then opened a story was shown a different earth.
 *
 * Sharing the function rather than the intent matters here, because the behaviour is not obvious:
 * imagery is added *beneath* the data and the vector fills are hidden rather than replaced, since
 * `map.setStyle()` would discard every source and layer the application has added. Two
 * implementations of that would drift.
 *
 * Source and layer ids are parameters, so each map owns its own raster layer and neither can remove
 * the other's.
 */
export interface BasemapTargets {
  readonly imagerySourceId: string;
  readonly imageryLayerId: string;
}

/** Vector fills recoloured per option, and hidden entirely under imagery. */
const TINTED_FILL_LAYER_IDS = [
  'background',
  'water',
  'landcover_wood',
  'landuse_park',
  'landuse_residential',
] as const;

/**
 * Recolours the vector base and adds, swaps or removes the imagery raster.
 *
 * Safe to call repeatedly: the raster layer and source are torn down and rebuilt each time, so
 * switching options does not accumulate layers.
 */
export function applyBasemap(
  map: MapLibreMap,
  option: BasemapOption,
  targets: BasemapTargets,
): void {
  // Under imagery the vector base is hidden entirely: the raster carries the ground, and leaving
  // coloured fills beneath it would tint the photograph.
  const underImagery = option.rasterTileUrl !== null;

  // Grayscale is a *dark* grey rather than a light one. The depth ramp is calibrated against a dark
  // ground — a pale base turns the unmeasured-depth grey muddy and drains the contrast the encoding
  // depends on. Neutral, not pale, is what a figure needs.
  const grayscale = option.id === 'grayscale';

  const land = grayscale ? '#16181a' : '#0f151d';
  const water = grayscale ? '#0b0c0d' : '#070a0f';
  const vegetation = grayscale ? '#1b1e20' : '#131a23';
  const residential = grayscale ? '#191c1e' : '#121821';

  const overrides: readonly { id: string; property: string; value: string }[] = [
    { id: 'background', property: 'background-color', value: land },
    { id: 'water', property: 'fill-color', value: water },
    { id: 'landcover_wood', property: 'fill-color', value: vegetation },
    { id: 'landuse_park', property: 'fill-color', value: vegetation },
    { id: 'landuse_residential', property: 'fill-color', value: residential },
    { id: 'landcover_ice_shelf', property: 'fill-color', value: land },
    { id: 'landcover_glacier', property: 'fill-color', value: land },
  ];

  for (const override of overrides) {
    // Guarded: the upstream style may rename or drop a layer, and a missing layer must not take the
    // whole map down.
    if (map.getLayer(override.id)) {
      map.setPaintProperty(override.id, override.property, override.value);
    }
  }

  // Fills are hidden under imagery, but labels and boundaries stay — place names are what make a
  // satellite view navigable rather than merely pretty.
  for (const id of TINTED_FILL_LAYER_IDS) {
    if (map.getLayer(id)) {
      map.setPaintProperty(
        id,
        id === 'background' ? 'background-opacity' : 'fill-opacity',
        underImagery ? 0 : 1,
      );
    }
  }

  applyImageryLayer(map, option, targets);
}

/**
 * Adds, swaps or removes the imagery layer beneath the data.
 *
 * One raster source reused across options rather than one per option, so switching does not
 * accumulate layers and there is a single place the beneath-the-data ordering is asserted.
 */
function applyImageryLayer(
  map: MapLibreMap,
  option: BasemapOption,
  targets: BasemapTargets,
): void {
  if (map.getLayer(targets.imageryLayerId)) {
    map.removeLayer(targets.imageryLayerId);
  }

  if (map.getSource(targets.imagerySourceId)) {
    map.removeSource(targets.imagerySourceId);
  }

  if (option.rasterTileUrl === null) {
    return;
  }

  map.addSource(targets.imagerySourceId, {
    type: 'raster',
    tiles: [option.rasterTileUrl],
    tileSize: 256,
    // Declared so MapLibre overzooms the deepest available tiles instead of showing nothing past the
    // source's limit — Blue Marble stops at zoom 8.
    maxzoom: option.maxZoom,
    attribution: option.attribution,
  });

  // Inserted beneath the first symbol layer, so imagery sits under place labels and under every
  // hazard overlay rather than covering them.
  const firstSymbol = map.getStyle().layers?.find((layer) => layer.type === 'symbol')?.id;

  map.addLayer(
    {
      id: targets.imageryLayerId,
      type: 'raster',
      source: targets.imagerySourceId,
      paint: { 'raster-opacity': 1 },
    },
    firstSymbol,
  );
}
