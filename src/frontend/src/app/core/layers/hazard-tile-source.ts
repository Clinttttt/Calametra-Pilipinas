import type { HazardLayer } from '../api/contracts';

/**
 * How a hazard layer's imagery is addressed, ready to hand to MapLibre.
 *
 * `tileSize` is not decoration: MapLibre uses it to decide which tiles to request for a viewport,
 * so it has to match the template. A cached tile is served at the size the publisher built the
 * cache at, and declaring a different one makes MapLibre ask for one tile where the cache holds
 * four — the proxy then returns a quarter of the ground stretched over the whole tile, which looks
 * like a projection error rather than a configuration one.
 */
export interface HazardRasterSource {
  readonly tiles: readonly [string];
  readonly tileSize: number;
}

/**
 * Size a cached tile is served at.
 *
 * The DOST-MGB susceptibility caches are built at 256 px, which the service reports in its own
 * `tileInfo`. This is the publisher's number, not a preference.
 */
const CACHED_TILE_SIZE = 256;

/**
 * Size requested when the publisher renders on demand.
 *
 * 512 is a deliberate saving on a rendered layer: one request covers four tiles' worth of ground,
 * so a viewport costs a quarter of the upstream renders. It is only available because the platform
 * is asking for an arbitrary bounding box rather than a tile from a fixed grid.
 */
const RENDERED_TILE_SIZE = 512;

/**
 * Chooses how to address a layer's imagery, and builds the template.
 *
 * **Why this is a function and not three lines inside the map component.** The choice is the
 * difference between a layer that pans and one that appears to hang: measured on 2026-09-11, the
 * MGB susceptibility services serve a cached tile in 60–120 ms and render the same tile in
 * 18.8–19.4 seconds. Inside `attachRasterLayer` that decision could only be verified by looking at
 * a map; here it is checked by tests, which is the same reason `radius-ring` and `cyclone-intensity`
 * are pure modules rather than inline logic.
 *
 * @param baseUrl Absolute base URL of the API. Absolute because MapLibre fetches these itself and
 *   never passes through Angular's `HttpClient`, so the interceptor that resolves root-relative
 *   paths does not apply.
 */
export function hazardRasterSource(layer: HazardLayer, baseUrl: string): HazardRasterSource {
  const root = `${trimTrailingSlashes(baseUrl)}/api/hazard-layers/${layer.id}`;

  return layer.supportsCachedTiles
    ? {
        // {z}/{x}/{y} in the platform's own route order. The proxy re-orders them into the
        // publisher's scheme — ArcGIS addresses a cached tile as level/row/column — so that
        // detail stays server-side where it is documented.
        tiles: [`${root}/tile/{z}/{x}/{y}`],
        tileSize: CACHED_TILE_SIZE,
      }
    : {
        // MapLibre substitutes the bounding box per tile. EPSG:3857 because that is what a raster
        // source speaks, and the PHIVOLCS service serves it despite advertising only EPSG:4326
        // (ADR-003).
        tiles: [
          `${root}/tile?bbox={bbox-epsg-3857}` +
            `&width=${RENDERED_TILE_SIZE}&height=${RENDERED_TILE_SIZE}`,
        ],
        tileSize: RENDERED_TILE_SIZE,
      };
}

/**
 * Removes trailing slashes from the configured base URL.
 *
 * The default config carries none, but the value is configurable at runtime so that a single built
 * artefact can be pointed at a different API. A trailing slash there would produce `//api/...`,
 * which most servers tolerate and some proxies do not.
 */
function trimTrailingSlashes(baseUrl: string): string {
  return baseUrl.replace(/\/+$/, '');
}
