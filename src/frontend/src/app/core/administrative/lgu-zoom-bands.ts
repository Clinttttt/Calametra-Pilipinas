import { LGU_MAX_ZOOM, LGU_MIN_ZOOM } from '../layers/lgu-boundary-source';

/**
 * Zoom bands for municipality boundaries, per ADR-005 D6.
 *
 * **Why bands at all.** Drawing sixteen hundred outlines at national zoom turns the archipelago into a
 * grey mesh: the lines are wider than the land between them, and the reader loses the country to its
 * own subdivisions. So the layer is absent nationally, faint regionally, and clear locally — and the
 * thresholds are here rather than inline so they can be checked without looking at a map.
 */

/**
 * Where boundaries first appear.
 *
 * Matches the tile floor: nothing is served below it, so drawing below it would show a layer with no
 * data and no explanation.
 */
export const LGU_BAND_REGIONAL = LGU_MIN_ZOOM;

/**
 * Where boundaries become clear and interactive.
 *
 * 9 is roughly a province filling the viewport. Below it a municipality is a few pixels across and a
 * click is a guess about which of several the reader meant; at and above it the outline is large enough
 * that pointing at one is unambiguous.
 */
export const LGU_BAND_LOCAL = 9;

/**
 * Opacity of the ordinary outline, interpolated across the bands.
 *
 * A function returning `unknown[]`, as `fault-style` does: a MapLibre expression is a heterogeneous
 * array — strings for the operator, numbers for the stops, nested arrays for the inputs — and typing it
 * as an array of numbers is what broke the build while the unit tests, which do not type-check, passed.
 */
export function lguLineOpacityExpression(): unknown[] {
  return [
    'interpolate',
    ['linear'],
    ['zoom'],
    // Faint where the band begins: present enough to say the country is subdivided, not so present that
    // it competes with the coastline or the hazard layers underneath.
    LGU_BAND_REGIONAL,
    0.18,
    LGU_BAND_LOCAL,
    0.55,
    LGU_MAX_ZOOM,
    0.75,
  ];
}

/** Width of the ordinary outline, in pixels. */
export function lguLineWidthExpression(): unknown[] {
  return [
    'interpolate',
    ['linear'],
    ['zoom'],
    // A hairline regionally. Anything thicker at this zoom reads as a road network.
    LGU_BAND_REGIONAL,
    0.4,
    LGU_BAND_LOCAL,
    0.9,
    LGU_MAX_ZOOM,
    1.4,
  ];
}

/**
 * The opacity stops alone, for the legend and for tests.
 *
 * Kept beside the expression so a change to one that is not made to the other is visible rather than
 * silent.
 */
export const LGU_OPACITY_STOPS: readonly number[] = [0.18, 0.55, 0.75];

/** The width stops alone, in pixels. */
export const LGU_WIDTH_STOPS: readonly number[] = [0.4, 0.9, 1.4];

/**
 * Whether the bulk layer should be drawn at a given zoom.
 *
 * A function rather than a `minzoom` alone because the caller also has to decide whether to bother
 * wiring hover, and because "off nationally" is a claim worth testing.
 */
export function lguBoundariesVisibleAt(zoom: number): boolean {
  return zoom >= LGU_BAND_REGIONAL;
}

/**
 * Whether pointing at a municipality is a meaningful gesture at a given zoom.
 *
 * Hover and click are wired only where an outline is big enough to aim at. Below the local band a click
 * would resolve to whichever of several small units happened to be under the cursor, which is worse
 * than not answering.
 */
export function lguInteractiveAt(zoom: number): boolean {
  return zoom >= LGU_BAND_LOCAL;
}

/**
 * Which band a zoom falls in, for the legend and for tests.
 */
export function lguBand(zoom: number): 'national' | 'regional' | 'local' {
  if (zoom < LGU_BAND_REGIONAL) {
    return 'national';
  }

  return zoom < LGU_BAND_LOCAL ? 'regional' : 'local';
}
