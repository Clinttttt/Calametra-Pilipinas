/**
 * How the current-LGU boundary tiles are addressed, ready to hand to MapLibre.
 *
 * **Why this is a module and not three lines in the map component.** The same reason
 * `hazard-tile-source` is: the URL template, the zoom range and the source-layer name are a contract
 * with the server, and a contract that can only be verified by looking at a map is a contract nobody
 * checks. It also keeps tile templates out of `CalametraApi`, which speaks `HttpClient` — MapLibre
 * fetches these itself and never passes through Angular's interceptors.
 */

/** Vector source configuration for the boundary tiles. */
export interface LguBoundarySource {
  readonly type: 'vector';
  readonly tiles: readonly [string];
  readonly minzoom: number;
  readonly maxzoom: number;
  readonly attribution: string;
}

/**
 * Name of the layer inside each tile.
 *
 * Set by the server's `ST_AsMVT` call. MapLibre needs it to address features, and getting it wrong
 * produces an empty map with no error — the tiles arrive and nothing matches.
 */
export const LGU_SOURCE_LAYER = 'lgu';

/**
 * Shallowest zoom the server builds.
 *
 * ADR-005 D6 does not draw municipality boundaries at national zoom, so nothing is served below 6.
 * Measured on the tiling view: a zoom-4 tile is 189 KB and takes about seven seconds because all
 * 1,611 outlines fall inside it, against 76 KB in 255 ms at zoom 6. A band the design does not draw
 * should not be requestable.
 */
export const LGU_MIN_ZOOM = 6;

/**
 * Deepest zoom the server builds.
 *
 * Past 14 a municipal outline is the frame rather than a feature in it, so MapLibre overzooms the
 * level-14 tile instead. That costs nothing and keeps the tile pyramid finite.
 */
export const LGU_MAX_ZOOM = 14;

/**
 * Attribution the layer must carry.
 *
 * CC BY 3.0 IGO is attribution-only — unlike the ODbL on the town centres it imposes no share-alike —
 * but attribution is still mandatory, and the licence names OCHA, NAMRIA and the PSA rather than
 * "Calametra".
 */
export const LGU_ATTRIBUTION =
  'Administrative boundaries: OCHA COD-AB, from NAMRIA and the Philippine Statistics Authority ' +
  '(CC BY 3.0 IGO). Land outlines, not municipal waters.';

/**
 * Builds the vector source for the current city and municipality land outlines.
 *
 * @param baseUrl Absolute base URL of the API. Absolute because MapLibre fetches tiles itself and
 *   never passes through Angular's `HttpClient`, so the interceptor that resolves root-relative paths
 *   does not apply — the same reason `hazardRasterSource` takes one.
 */
export function lguBoundarySource(baseUrl: string): LguBoundarySource {
  const root = `${trimTrailingSlashes(baseUrl)}/api/lgu-boundaries`;

  return {
    type: 'vector',
    tiles: [`${root}/tile/{z}/{x}/{y}`],
    minzoom: LGU_MIN_ZOOM,
    maxzoom: LGU_MAX_ZOOM,
    attribution: LGU_ATTRIBUTION,
  };
}

/**
 * Removes trailing slashes from the configured base URL.
 *
 * The default config carries none, but the value is configurable at runtime so one built artefact can
 * be pointed at a different API. A trailing slash there would produce `//api/...`, which most servers
 * tolerate and some proxies do not.
 */
function trimTrailingSlashes(baseUrl: string): string {
  return baseUrl.replace(/\/+$/, '');
}
