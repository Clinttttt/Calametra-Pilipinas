import { type DepthReading, type MagnitudeReading } from '../api/contracts';

/**
 * VISUAL ENCODING
 *
 * Depth is encoded as hue, magnitude as size.
 *
 * That split is the standard seismological convention and it is load-bearing
 * here: two variables competing for the same channel makes both unreadable, and
 * the map has to show magnitude and depth simultaneously. Following the
 * convention also means anyone who has read a seismicity map can read this one
 * without consulting a legend.
 *
 * The hex values below are duplicated from `styles/_tokens.scss` because
 * MapLibre paint expressions are evaluated on a WebGL canvas and cannot read
 * CSS custom properties. `DEPTH_BANDS` is the single source for both the map
 * layers and the legend component, so the duplication is confined to one file
 * and the legend can never disagree with the markers.
 */

export type DepthBandId = 'shallow' | 'upper' | 'mid' | 'lower' | 'deepest' | 'unknown';

export interface DepthBand {
  readonly id: DepthBandId;
  readonly colour: string;
  /** Inclusive lower bound in kilometres. */
  readonly fromKm: number;
  /** Exclusive upper bound in kilometres, or null for the open-ended band. */
  readonly toKm: number | null;
  readonly label: string;
}

/** Shallow-warm to deep-cool, the conventional seismicity depth ramp. */
export const DEPTH_BANDS: readonly DepthBand[] = [
  { id: 'shallow', colour: '#e8563f', fromKm: 0, toKm: 30, label: '0–30 km' },
  { id: 'upper', colour: '#e8913c', fromKm: 30, toKm: 70, label: '30–70 km' },
  { id: 'mid', colour: '#ddc85c', fromKm: 70, toKm: 150, label: '70–150 km' },
  { id: 'lower', colour: '#5fb9a8', fromKm: 150, toKm: 300, label: '150–300 km' },
  { id: 'deepest', colour: '#5a7fc0', fromKm: 300, toKm: null, label: '300 km +' },
];

/**
 * Colour for a depth that was not measured.
 *
 * Deliberately inert — a desaturated grey that sits behind every band in the
 * ramp. An assigned depth of 10 km must not be rendered in the same confident
 * red as a measured 10 km, because they are not the same claim.
 */
export const UNMEASURED_DEPTH_COLOUR = '#4a5464';

/** Which band a depth falls in. Returns `unknown` for unmeasured depths. */
export function depthBandFor(depth: DepthReading): DepthBandId {
  if (!depth.isMeasured || depth.kilometres === null) {
    return 'unknown';
  }

  const kilometres = depth.kilometres;

  const band = DEPTH_BANDS.find(
    (candidate) => kilometres >= candidate.fromKm && (candidate.toKm === null || kilometres < candidate.toKm),
  );

  return band?.id ?? 'unknown';
}

export function depthColourFor(depth: DepthReading): string {
  const bandId = depthBandFor(depth);

  if (bandId === 'unknown') {
    return UNMEASURED_DEPTH_COLOUR;
  }

  return DEPTH_BANDS.find((band) => band.id === bandId)!.colour;
}

/**
 * Marker radius in pixels for a magnitude.
 *
 * Scaled by energy rather than linearly. Magnitude is logarithmic, so a linear
 * radius makes an M7 look only twice an M3.5 when it releases many orders of
 * magnitude more energy. Radius is proportional to `10^(0.35 M)`, normalised,
 * which reads as a meaningful difference without letting one large event cover
 * the region.
 *
 * Clamped at both ends: below the floor markers become unclickable, above the
 * ceiling they obscure the geography they are supposed to locate.
 */
export function markerRadiusFor(magnitude: MagnitudeReading | null): number {
  const minRadius = 3;
  const maxRadius = 22;

  if (magnitude === null) {
    return minRadius;
  }

  const scaled = 10 ** (0.35 * magnitude.value) / 10 ** (0.35 * 2);
  const radius = minRadius + Math.sqrt(scaled) * 1.6;

  return Math.min(maxRadius, Math.max(minRadius, radius));
}

/**
 * Colour for a depth expressed as plain kilometres.
 *
 * A scalar companion to {@link depthColourFor}, for the cross-section, where points
 * arrive as `{ depthKm, depthMeasured }` rather than as a `DepthReading`. Lives here
 * rather than in the plot component so the section, the map and the legend cannot drift
 * apart — `DEPTH_BANDS` stays the one place the ramp is defined.
 */
export function depthColourForKilometres(kilometres: number, measured: boolean): string {
  if (!measured) {
    return UNMEASURED_DEPTH_COLOUR;
  }

  const band = DEPTH_BANDS.find(
    (candidate) =>
      kilometres >= candidate.fromKm && (candidate.toKm === null || kilometres < candidate.toKm),
  );

  return band?.colour ?? UNMEASURED_DEPTH_COLOUR;
}

/**
 * Marker radius for a magnitude expressed as a plain number.
 *
 * Shares {@link markerRadiusFor}'s energy scaling so a magnitude reads as the same size
 * on the cross-section as it does on the map. Null means the source reported no
 * magnitude, which still gets a mark: the event has a real depth, and depth is what the
 * section is about.
 */
export function markerRadiusForMagnitude(magnitude: number | null): number {
  if (magnitude === null) {
    return 3;
  }

  return markerRadiusFor({ value: magnitude, scale: 'Mw', scaleFamily: 'Moment', display: '' });
}

/**
 * MapLibre paint expression for marker radius.
 *
 * Two constraints shape the odd-looking structure here.
 *
 * First, MapLibre requires a `["zoom"]` expression to be the **outermost** expression
 * of a paint property — it may not be nested inside arithmetic. So this cannot be
 * written as `["*", <magnitude interpolate>, <zoom interpolate>]`, which is the
 * obvious formulation and silently fails to render.
 *
 * Second, magnitude and zoom both need to affect radius. The way to satisfy both is
 * to put zoom at the top level and nest a *separate*, pre-multiplied magnitude
 * interpolation inside each zoom stop.
 *
 * Zoom scaling exists because the archipelago spans ~1,800 km. At the national
 * overview, ~8,700 events pile onto the trench systems, and at full marker size they
 * merge into an opaque mass that hides the coastline. Markers shrink as the view
 * widens so the cluster reads as a distribution, and return to full size when the
 * user zooms into a specific area. The floor is deliberately not aggressive: markers
 * below roughly 2.5 px disappear entirely on a dark basemap, which is worse than
 * overlap.
 */
export function magnitudeRadiusExpression(): unknown[] {
  const magnitudes = [2, 3, 4, 5, 6, 7, 8];

  /** Magnitude interpolation with every radius multiplied by `scale`. */
  const scaledByMagnitude = (scale: number): unknown[] => [
    'interpolate',
    ['linear'],
    ['coalesce', ['get', 'magnitude'], 2],
    ...magnitudes.flatMap((magnitude) => [
      magnitude,
      Number(
        (
          markerRadiusFor({ value: magnitude, scale: 'Mw', scaleFamily: 'Moment', display: '' }) *
          scale
        ).toFixed(2),
      ),
    ]),
  ];

  // Zoom stops, outermost as the spec requires.
  const zoomStops: readonly [number, number][] = [
    [4.5, 0.62],
    [6, 0.72],
    [8, 0.85],
    [10, 1],
    [13, 1.3],
  ];

  return [
    'interpolate',
    ['linear'],
    ['zoom'],
    ...zoomStops.flatMap(([zoom, scale]) => [zoom, scaledByMagnitude(scale)]),
  ];
}

/**
 * Radius expression for the ring that marks an agency-assigned depth.
 *
 * A separate builder rather than `["+", radiusExpression, n]`, for the same reason:
 * the zoom expression has to stay outermost, so the offset is folded into the
 * per-stop magnitude radii instead of added around the outside.
 */
export function assignedDepthRingRadiusExpression(): unknown[] {
  const magnitudes = [2, 3, 4, 5, 6, 7, 8];
  const ringOffsetPixels = 2.5;

  const scaledByMagnitude = (scale: number): unknown[] => [
    'interpolate',
    ['linear'],
    ['coalesce', ['get', 'magnitude'], 2],
    ...magnitudes.flatMap((magnitude) => [
      magnitude,
      Number(
        (
          markerRadiusFor({ value: magnitude, scale: 'Mw', scaleFamily: 'Moment', display: '' }) *
            scale +
          ringOffsetPixels
        ).toFixed(2),
      ),
    ]),
  ];

  const zoomStops: readonly [number, number][] = [
    [4.5, 0.62],
    [6, 0.72],
    [8, 0.85],
    [10, 1],
    [13, 1.3],
  ];

  return [
    'interpolate',
    ['linear'],
    ['zoom'],
    ...zoomStops.flatMap(([zoom, scale]) => [zoom, scaledByMagnitude(scale)]),
  ];
}

/** MapLibre paint expression selecting a depth colour per feature. */
export function depthColourExpression(): unknown[] {
  const bandStops = DEPTH_BANDS.flatMap((band) => [band.id, band.colour]);

  return [
    'case',
    // Unmeasured depths first: this predicate wins regardless of the value.
    ['!', ['coalesce', ['get', 'depthMeasured'], false]],
    UNMEASURED_DEPTH_COLOUR,
    ['match', ['coalesce', ['get', 'depthBand'], 'unknown'], ...bandStops, UNMEASURED_DEPTH_COLOUR],
  ];
}
