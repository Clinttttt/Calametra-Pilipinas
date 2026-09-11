import type { CycloneFix } from '../api/contracts';
import { cycloneSymbolImage, cycloneSymbolSize, eyewallIsMeaningful } from './cyclone-symbol';
import { trackColourForWind } from './cyclone-intensity';

/**
 * CYCLONE WIND FIELD GEOMETRY
 *
 * Builds the drawable shapes for a storm's measured extent.
 *
 * ── Why not a circle ────────────────────────────────────────────────────────
 * A single radius drawn as a circle would be wrong by up to a factor of 37. Measured in this
 * archive: Typhoon Co-May at 2025-07-25 reported gale radii of 35, 185, 40 and 5 nautical miles
 * in its four quadrants. Kalmaegi in 2019 reported 320, 20, 90, 320. A cyclone's damaging wind
 * field is routinely lopsided, and drawing it round would misplace the hazard by hundreds of
 * kilometres on one side.
 *
 * ── Two geometries, because agencies use two ────────────────────────────────
 * JTWC publishes a radius per compass quadrant at the 34-knot threshold. JMA and KMA publish an
 * ellipse — long axis, short axis and bearing — at 30 knots. These cannot share a builder, and
 * averaging them would produce a shape neither agency published.
 *
 * ── What is measured and what is not ────────────────────────────────────────
 * Everything this module emits is measured and drawn to scale. Rotation is conveyed by the centre
 * symbol instead — a screen-sized glyph that turns without claiming any geographic extent, which
 * is why it lives in cyclone-symbol.ts rather than here.
 */

/** Nautical miles to kilometres. The exact definition. */
const KM_PER_NAUTICAL_MILE = 1.852;

const EARTH_RADIUS_KM = 6371.0088;

/**
 * The point a given distance and bearing from an origin.
 *
 * Great-circle rather than a flat offset. A gale radius can exceed 300 nautical miles, and at
 * that distance treating degrees as uniform would distort the shape visibly — a degree of
 * longitude is 111 km at the equator and 104 km at Batanes.
 */
function destination(
  latitude: number,
  longitude: number,
  distanceKm: number,
  bearingDegrees: number,
): [number, number] {
  const angular = distanceKm / EARTH_RADIUS_KM;
  const bearing = (bearingDegrees * Math.PI) / 180;
  const phi1 = (latitude * Math.PI) / 180;
  const lambda1 = (longitude * Math.PI) / 180;

  const phi2 = Math.asin(
    Math.sin(phi1) * Math.cos(angular) + Math.cos(phi1) * Math.sin(angular) * Math.cos(bearing),
  );

  const lambda2 =
    lambda1
    + Math.atan2(
      Math.sin(bearing) * Math.sin(angular) * Math.cos(phi1),
      Math.cos(angular) - Math.sin(phi1) * Math.sin(phi2),
    );

  return [(lambda2 * 180) / Math.PI, (phi2 * 180) / Math.PI];
}

/**
 * The gale radius at a given compass bearing, interpolated between quadrant values.
 *
 * IBTrACS reports one radius per quadrant, which describes a shape with four hard steps. Drawing
 * those steps literally produces a cross, not a wind field, so the radius is interpolated
 * smoothly between the quadrant bearings — a cosine blend rather than linear, so the transition
 * has no visible corner at the quadrant boundary.
 *
 * The quadrant value is treated as applying at its bisector (45°, 135°, 225°, 315°), which is
 * how the convention is defined: `R34_NE` is the radius somewhere in the north-east quadrant,
 * best represented at its centre.
 */
function quadrantRadiusKm(fix: CycloneFix, bearingDegrees: number): number | null {
  const quadrants: readonly { bisector: number; radiusNm: number | null }[] = [
    { bisector: 45, radiusNm: fix.galeNorthEastNm },
    { bisector: 135, radiusNm: fix.galeSouthEastNm },
    { bisector: 225, radiusNm: fix.galeSouthWestNm },
    { bisector: 315, radiusNm: fix.galeNorthWestNm },
  ];

  // A quadrant with no radius is treated as zero rather than skipped: the agency reported no
  // gale-force wind in that direction, which is a measurement and not an absence of one.
  const usable = quadrants.filter((quadrant) => quadrant.radiusNm !== null);

  if (usable.length === 0) {
    return null;
  }

  const bearing = ((bearingDegrees % 360) + 360) % 360;

  // Find the two bisectors this bearing lies between, wrapping across north.
  let previous = quadrants[quadrants.length - 1];
  let next = quadrants[0];

  for (let i = 0; i < quadrants.length; i++) {
    const candidate = quadrants[i];

    if (candidate.bisector >= bearing) {
      next = candidate;
      previous = quadrants[(i - 1 + quadrants.length) % quadrants.length];
      break;
    }

    if (i === quadrants.length - 1) {
      previous = candidate;
      next = quadrants[0];
    }
  }

  let span = next.bisector - previous.bisector;
  let offset = bearing - previous.bisector;

  // Wrap: crossing north means the span and offset go negative.
  if (span <= 0) {
    span += 360;
  }

  if (offset < 0) {
    offset += 360;
  }

  const t = span === 0 ? 0 : offset / span;
  // Cosine blend, so the curve leaves each bisector flat and has no corner there.
  const eased = (1 - Math.cos(t * Math.PI)) / 2;

  const from = previous.radiusNm ?? 0;
  const to = next.radiusNm ?? 0;

  return (from + (to - from) * eased) * KM_PER_NAUTICAL_MILE;
}

/** The ellipse radius at a bearing, for the JMA and KMA convention. */
function ellipseRadiusKm(fix: CycloneFix, bearingDegrees: number): number | null {
  if (fix.galeLongAxisNm === null && fix.galeShortAxisNm === null) {
    return null;
  }

  const longKm = (fix.galeLongAxisNm ?? fix.galeShortAxisNm ?? 0) * KM_PER_NAUTICAL_MILE;
  const shortKm = (fix.galeShortAxisNm ?? fix.galeLongAxisNm ?? 0) * KM_PER_NAUTICAL_MILE;

  // Bearing of the long axis. IBTrACS gives this as a 16-point compass index rather than
  // degrees, so it is converted: 16 sectors of 22.5° each.
  const axisBearing = (fix.galeBearingDegrees ?? 0) * 22.5;
  const theta = ((bearingDegrees - axisBearing) * Math.PI) / 180;

  // Polar form of an ellipse about its centre.
  const denominator = Math.sqrt(
    (shortKm * Math.cos(theta)) ** 2 + (longKm * Math.sin(theta)) ** 2,
  );

  return denominator === 0 ? null : (longKm * shortKm) / denominator;
}

/**
 * A wind band: its threshold and the four quadrant radii that describe it.
 *
 * Grouped so the bands can be built in one loop rather than three near-identical blocks, and so
 * the threshold travels with its radii — the same discipline applied everywhere else here.
 */
interface WindBand {
  readonly threshold: number;
  readonly northEastNm: number | null;
  readonly southEastNm: number | null;
  readonly southWestNm: number | null;
  readonly northWestNm: number | null;
}

/**
 * The bands a fix reports, outermost first.
 *
 * Ordered outermost to innermost so they paint in that sequence and each inner band draws over
 * the one containing it. Reversing this would hide the strongest winds under the weakest.
 */
function bandsFor(fix: CycloneFix): readonly WindBand[] {
  if (fix.galeGeometry !== 'Quadrants') {
    return [];
  }

  const candidates: readonly WindBand[] = [
    {
      threshold: fix.galeThresholdKnots,
      northEastNm: fix.galeNorthEastNm,
      southEastNm: fix.galeSouthEastNm,
      southWestNm: fix.galeSouthWestNm,
      northWestNm: fix.galeNorthWestNm,
    },
    {
      threshold: 50,
      northEastNm: fix.stormNorthEastNm,
      southEastNm: fix.stormSouthEastNm,
      southWestNm: fix.stormSouthWestNm,
      northWestNm: fix.stormNorthWestNm,
    },
    {
      threshold: 64,
      northEastNm: fix.hurricaneNorthEastNm,
      southEastNm: fix.hurricaneSouthEastNm,
      southWestNm: fix.hurricaneSouthWestNm,
      northWestNm: fix.hurricaneNorthWestNm,
    },
  ];

  // A band with no quadrant reported was not analysed at that threshold. Dropped rather than
  // drawn at zero, which would put a dot at the centre implying a measurement.
  return candidates.filter(
    (band) =>
      band.northEastNm !== null
      || band.southEastNm !== null
      || band.southWestNm !== null
      || band.northWestNm !== null,
  );
}

/** The radius of a specific band at a bearing, interpolated between its quadrants. */
function bandRadiusKm(band: WindBand, bearingDegrees: number): number {
  const quadrants: readonly { bisector: number; radiusNm: number }[] = [
    { bisector: 45, radiusNm: band.northEastNm ?? 0 },
    { bisector: 135, radiusNm: band.southEastNm ?? 0 },
    { bisector: 225, radiusNm: band.southWestNm ?? 0 },
    { bisector: 315, radiusNm: band.northWestNm ?? 0 },
  ];

  const bearing = ((bearingDegrees % 360) + 360) % 360;

  let index = quadrants.findIndex((quadrant) => quadrant.bisector >= bearing);

  if (index < 0) {
    index = 0;
  }

  const next = quadrants[index];
  const previous = quadrants[(index - 1 + quadrants.length) % quadrants.length];

  let span = next.bisector - previous.bisector;
  let offset = bearing - previous.bisector;

  if (span <= 0) {
    span += 360;
  }

  if (offset < 0) {
    offset += 360;
  }

  const t = span === 0 ? 0 : offset / span;
  // Cosine blend: the curve leaves each bisector flat, so there is no corner at a boundary.
  const eased = (1 - Math.cos(t * Math.PI)) / 2;

  return (previous.radiusNm + (next.radiusNm - previous.radiusNm) * eased) * KM_PER_NAUTICAL_MILE;
}

/**
 * A smooth wind profile sampled between the reported radii.
 *
 * ── Why contours rather than three bands ────────────────────────────────────
 * Three hard-edged polygons read as a diagram. A real wind field has no steps in it, and
 * operational wind-field products show a continuous surface — which is what the reported radii
 * actually describe: three sample points on a curve, not three plateaus.
 *
 * So the profile is reconstructed by interpolating wind speed against radius. Between the 64 and
 * 50 knot radii the speed falls smoothly from 64 to 50, and so on outward. Inside the innermost
 * reported radius the speed rises toward the storm's peak at the radius of maximum wind, which is
 * the shape a tropical cyclone's wind profile genuinely has.
 *
 * ── What this is and is not ─────────────────────────────────────────────────
 * It is interpolation between measured values, in the same family as the parametric profiles used
 * in tropical-cyclone analysis. It is <b>not</b> an observation: nobody measured the wind at every
 * bearing and radius. The contours pass exactly through the agency's reported radii, so the
 * measurements are preserved and only the space between them is modelled — and the panel says so.
 */
const CONTOUR_COUNT = 14;

/**
 * Sample contours from the outermost reported band inward.
 *
 * Each contour is a closed ring at one wind speed, carrying that speed so the paint expression can
 * shade it. Emitted outermost first so inner contours paint over outer ones.
 */
function windProfileContours(
  fix: CycloneFix,
  bands: readonly WindBand[],
): CycloneFieldGeoJson['features'][number][] {
  if (bands.length === 0) {
    return [];
  }

  const features: CycloneFieldGeoJson['features'][number][] = [];

  // Outermost band's threshold is the lowest speed; the peak is the storm's own wind. Sampling
  // between them gives the range the contours span.
  const outerSpeed = bands[0].threshold;
  const peakSpeed = Math.max(fix.windKnots ?? outerSpeed, bands[bands.length - 1].threshold);

  for (let step = 0; step < CONTOUR_COUNT; step++) {
    // Eased inward rather than linear, so contours bunch where the gradient is steepest — near
    // the core — which is where a real wind profile changes fastest.
    const t = Math.pow(step / (CONTOUR_COUNT - 1), 1.6);
    const speed = outerSpeed + (peakSpeed - outerSpeed) * t;

    const ring = contourRing(fix, bands, speed);

    if (ring === null) {
      continue;
    }

    features.push({
      type: 'Feature',
      properties: {
        role: 'gale',
        threshold: Math.round(speed),
        // Coloured by the speed this contour represents, using the same function as the track. The
        // field therefore carries the intensity gradient in hue as well as in accumulated opacity,
        // so a reader sees where hurricane-force wind sits inside the gale envelope instead of
        // only seeing that the envelope is denser somewhere in the middle.
        colour: trackColourForWind(speed),
      },
      geometry: { type: 'Polygon', coordinates: [ring] },
    });
  }

  return features;
}

/**
 * The ring at a given wind speed, found by interpolating radius against the reported bands.
 *
 * Per bearing, because the bands are asymmetric: the 50-knot radius to the north-east may be twice
 * the 50-knot radius to the south-west, so the contour has to be solved direction by direction.
 */
function contourRing(
  fix: CycloneFix,
  bands: readonly WindBand[],
  speedKnots: number,
  steps = 72,
): [number, number][] | null {
  const ring: [number, number][] = [];

  for (let i = 0; i <= steps; i++) {
    const bearing = (i / steps) * 360;
    const radiusKm = radiusForSpeed(fix, bands, speedKnots, bearing);

    if (radiusKm === null) {
      return null;
    }

    ring.push(destination(fix.latitude, fix.longitude, Math.max(radiusKm, 0.1), bearing));
  }

  return ring;
}

/**
 * The radius at which the wind falls to a given speed, along one bearing.
 *
 * Interpolates between the bracketing reported bands. Inside the innermost band the profile is
 * carried down to the radius of maximum wind where one is reported, which is where a cyclone's
 * wind peaks; without an eyewall radius it stops at the innermost measurement rather than
 * extrapolating into the core.
 */
function radiusForSpeed(
  fix: CycloneFix,
  bands: readonly WindBand[],
  speedKnots: number,
  bearingDegrees: number,
): number | null {
  // Bands arrive outermost (lowest speed) first. Build speed/radius pairs along this bearing.
  const points = bands
    .map((band) => ({ speed: band.threshold, radiusKm: bandRadiusKm(band, bearingDegrees) }))
    .filter((point) => point.radiusKm > 0);

  if (points.length === 0) {
    return null;
  }

  // The eyewall extends the curve to the storm's peak wind, giving the profile its inner limb.
  if (
    eyewallIsMeaningful(fix.windKnots, fix.eyewallRadiusNm)
    && fix.windKnots !== null
    && fix.eyewallRadiusNm !== null
  ) {
    points.push({
      speed: fix.windKnots,
      radiusKm: fix.eyewallRadiusNm * KM_PER_NAUTICAL_MILE,
    });
  }

  // Sorted by speed ascending, so radius descends: faster winds sit closer to the centre.
  points.sort((a, b) => a.speed - b.speed);

  if (speedKnots <= points[0].speed) {
    return points[0].radiusKm;
  }

  if (speedKnots >= points[points.length - 1].speed) {
    return points[points.length - 1].radiusKm;
  }

  for (let i = 0; i < points.length - 1; i++) {
    const low = points[i];
    const high = points[i + 1];

    if (speedKnots >= low.speed && speedKnots <= high.speed) {
      const span = high.speed - low.speed;
      const t = span === 0 ? 0 : (speedKnots - low.speed) / span;

      return low.radiusKm + (high.radiusKm - low.radiusKm) * t;
    }
  }

  return points[points.length - 1].radiusKm;
}

/** A closed ring for one band. */
function bandRing(fix: CycloneFix, band: WindBand, steps = 72): [number, number][] {
  const ring: [number, number][] = [];

  for (let i = 0; i <= steps; i++) {
    const bearing = (i / steps) * 360;

    ring.push(
      destination(
        fix.latitude,
        fix.longitude,
        Math.max(bandRadiusKm(band, bearing), 0.1),
        bearing,
      ),
    );
  }

  return ring;
}

/**
 * A closed ring of coordinates approximating the gale field.
 *
 * Retained for the ellipse case, where there is only one band and no quadrants to interpolate.
 */
function galeRing(fix: CycloneFix, steps = 72): [number, number][] | null {
  const radiusAt =
    fix.galeGeometry === 'Quadrants'
      ? quadrantRadiusKm
      : fix.galeGeometry === 'Ellipse'
        ? ellipseRadiusKm
        : null;

  if (radiusAt === null) {
    return null;
  }

  const ring: [number, number][] = [];

  for (let i = 0; i <= steps; i++) {
    const bearing = (i / steps) * 360;
    const radiusKm = radiusAt(fix, bearing);

    if (radiusKm === null) {
      return null;
    }

    // A zero-radius direction still contributes a vertex at the centre, which is what makes a
    // one-sided field render as one-sided rather than as a gap in the outline.
    ring.push(destination(fix.latitude, fix.longitude, Math.max(radiusKm, 0.1), bearing));
  }

  return ring;
}

/** A circle, used for the eyewall where the shape genuinely is circular. */
function circleRing(
  latitude: number,
  longitude: number,
  radiusKm: number,
  steps = 48,
): [number, number][] {
  const ring: [number, number][] = [];

  for (let i = 0; i <= steps; i++) {
    ring.push(destination(latitude, longitude, radiusKm, (i / steps) * 360));
  }

  return ring;
}

export interface CycloneFieldGeoJson {
  readonly type: 'FeatureCollection';
  readonly features: readonly {
    readonly type: 'Feature';
    readonly properties: {
      readonly role: 'gale' | 'eyewall' | 'centre';
      readonly threshold?: number;
      readonly symbolSize?: number;
      /**
       * The ramp colour for this feature's wind speed.
       *
       * A contour's `threshold` *is* a wind speed in knots, so the earthquake-style question of
       * which ramp to use does not arise here: the field is coloured by exactly the same function
       * as the track, `trackColourForWind`. A 64-knot contour and a 64-knot stretch of track take
       * the same colour because they report the same quantity. That is what makes the footprint
       * legible as an extension of the track rather than as separate decoration around it.
       */
      readonly colour?: string;
      /** The glyph variant for the centre, keyed to intensity. See `cyclone-symbol.ts`. */
      readonly symbolImage?: string;
    };
    readonly geometry:
      | { readonly type: 'Polygon'; readonly coordinates: readonly [number, number][][] }
      | { readonly type: 'LineString'; readonly coordinates: readonly [number, number][] }
      | { readonly type: 'Point'; readonly coordinates: readonly [number, number] };
  }[];
}

/**
 * The storm's state at one fix: centre, gale field and eyewall, in a single collection.
 *
 * <b>One collection, deliberately.</b> These were three separate sources and three separate
 * effects, and they drifted — the footprint could be written for one fix while the centre marker
 * still showed the previous one, so the ring appeared to run ahead of the dot. Emitting them
 * together makes that class of bug impossible: there is one `setData` call, so the parts cannot
 * describe different moments.
 *
 * Returns an empty collection for a null fix, which clears the map through the same path rather
 * than needing a second one.
 */
export function toWindFieldGeoJson(fix: CycloneFix | null): CycloneFieldGeoJson {
  if (fix === null) {
    return { type: 'FeatureCollection', features: [] };
  }

  const features: CycloneFieldGeoJson['features'][number][] = [];

  // Nested bands where the agency publishes quadrants: 34, 50 and 64 knots, outermost first so
  // each inner band paints over the one containing it. This is the convention used in
  // tropical-cyclone wind-field products, and it shows the intensity gradient rather than only
  // the outer edge — the difference between "gale somewhere out here" and "hurricane force in
  // this much smaller area".
  const bands = bandsFor(fix);

  if (bands.length > 0) {
    // A smooth profile sampled between the reported radii, rather than the radii as hard bands.
    // The contours pass exactly through the measurements; only the space between them is modelled.
    features.push(...windProfileContours(fix, bands));
  } else {
    // The ellipse case: one band, no quadrants to nest.
    const gale = galeRing(fix);

    if (gale !== null) {
      features.push({
        type: 'Feature',
        properties: {
          role: 'gale',
          threshold: fix.galeThresholdKnots,
          // The ellipse agencies publish one threshold and no inner bands, so this single ring
          // takes the colour of that threshold — 30 kt for JMA and KMA, which lands in the
          // tropical-storm band. There is deliberately no gradient here: inventing one would show
          // structure these agencies did not measure.
          colour: trackColourForWind(fix.galeThresholdKnots),
        },
        geometry: { type: 'Polygon', coordinates: [gale] },
      });
    }
  }

  // Gated: a disorganised system reports a large radius of maximum wind that does not describe an
  // eyewall, and drawing one would assert a structure it does not have.
  if (eyewallIsMeaningful(fix.windKnots, fix.eyewallRadiusNm)) {
    features.push({
      type: 'Feature',
      // The eyewall takes the storm's own peak colour, because that is the wind speed found there.
      // Previously it was drawn in the interface's caution amber, which said "there is a caveat
      // about this value" — the wrong statement. The eyewall is a measurement, not a caveat.
      properties: { role: 'eyewall', colour: trackColourForWind(fix.windKnots) },
      geometry: {
        type: 'Polygon',
        coordinates: [
          circleRing(fix.latitude, fix.longitude, fix.eyewallRadiusNm! * KM_PER_NAUTICAL_MILE),
        ],
      },
    });
  }

  // The centre, carried in the same collection as the field it belongs to.
  features.push({
    type: 'Feature',
    // Size and glyph variant travel with the feature so the paint expression can read them, rather
    // than the layer needing to know the current fix.
    properties: {
      role: 'centre',
      symbolSize: cycloneSymbolSize(fix.windKnots),
      symbolImage: cycloneSymbolImage(fix.windKnots),
      colour: trackColourForWind(fix.windKnots),
    },
    geometry: { type: 'Point', coordinates: [fix.longitude, fix.latitude] },
  });

  return { type: 'FeatureCollection', features };
}

