/**
 * STORY SPOTLIGHT
 *
 * Builds the mask that dims the map outside the area a story beat is describing.
 *
 * ── Why a mask rather than a ring ───────────────────────────────────────────
 * The first attempt drew a gold ring around the subject. Drawing a circle on a map is an annotation
 * laid over cartography — it reads as a marker pen, it competes with the data it encloses, and at any
 * zoom where the subject has real extent the ring is simply the wrong size.
 *
 * A mask inverts the problem. Instead of adding a mark, it removes emphasis from everywhere else: one
 * polygon covering the world with a hole cut where the reader should be looking. The map itself
 * carries the emphasis, nothing is drawn over the subject, and the technique is the standard one in
 * published cartography and data journalism for exactly this purpose.
 *
 * ── Why the hole is sized in screen space ───────────────────────────────────
 * The radius is derived from the beat's zoom rather than written into content. A fixed geographic
 * radius would be a pinprick at national zoom and larger than the viewport at city zoom, so every
 * beat would need a hand-tuned figure that then had to be re-tuned whenever its camera moved.
 *
 * Converting a target *screen* radius to ground distance keeps the lit area a consistent size in the
 * reader's eye at every zoom, which is what makes the effect feel deliberate rather than arbitrary.
 */

/** Earth's mean radius, for the great-circle offsets below. */
const EARTH_RADIUS_KM = 6371.0088;

/**
 * Ground resolution at the equator for zoom 0, in metres per pixel.
 *
 * The Web Mercator constant: 2 * pi * 6378137 / 256. Scaled by cos(latitude) and halved per zoom
 * level to give the resolution at a given place and zoom.
 */
const EQUATOR_METRES_PER_PIXEL = 156543.03392;

/**
 * The lit radius in screen pixels.
 *
 * Chosen to sit comfortably inside the map pane once the story panel takes the right-hand third: the
 * subject is lit with room around it, rather than the mask hugging the epicentre so tightly that it
 * reads as a ring again.
 */
const SPOTLIGHT_SCREEN_RADIUS_PX = 300;

/** The point a given distance and bearing from an origin, on a great circle. */
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

/** The ground distance covered by the target screen radius at this latitude and zoom. */
export function spotlightRadiusKm(latitude: number, zoom: number): number {
  const metresPerPixel =
    (EQUATOR_METRES_PER_PIXEL * Math.cos((latitude * Math.PI) / 180)) / Math.pow(2, zoom);

  return (metresPerPixel * SPOTLIGHT_SCREEN_RADIUS_PX) / 1000;
}

/**
 * A world-covering polygon with a circular hole at the given point.
 *
 * The outer ring is wound clockwise and the hole anticlockwise, which is what GeoJSON requires for an
 * interior ring — reversed, renderers fill the hole instead of cutting it.
 *
 * The outer ring stops short of the poles at ±85°, the Web Mercator limit. Using ±90 produces
 * coordinates the projection cannot represent and the fill collapses.
 */
/**
 * A polygon feature, described structurally.
 *
 * Declared here rather than using the ambient `GeoJSON` namespace, which is not in scope in this
 * project's TypeScript configuration. Structural typing is enough: the value is handed straight to
 * MapLibre's `setData`.
 */
export interface SpotlightFeature {
  readonly type: 'Feature';
  readonly properties: Record<string, never>;
  readonly geometry: {
    readonly type: 'Polygon';
    readonly coordinates: readonly [number, number][][];
  };
}

export function spotlightMask(
  latitude: number,
  longitude: number,
  radiusKm: number,
  steps = 96,
): SpotlightFeature {
  const hole: [number, number][] = [];

  // Anticlockwise, so this reads as an interior ring.
  for (let i = steps; i >= 0; i--) {
    hole.push(destination(latitude, longitude, radiusKm, (i / steps) * 360));
  }

  const world: [number, number][] = [
    [-180, -85],
    [180, -85],
    [180, 85],
    [-180, 85],
    [-180, -85],
  ];

  return {
    type: 'Feature',
    properties: {},
    geometry: { type: 'Polygon', coordinates: [world, hole] },
  };
}
