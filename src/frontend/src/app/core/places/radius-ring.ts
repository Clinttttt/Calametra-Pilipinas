/**
 * The circle a radius search actually describes, as drawable geometry.
 *
 * Drawn rather than left implicit because the number in the panel is not self-explanatory: the
 * radius is measured from a representative point for the place, not from its boundary, and a
 * reader can only see that the circle is centred on a point rather than following the town
 * limits if the circle is on the map.
 *
 * **Computed on the sphere, not by offsetting degrees.** A degree of longitude is 111 km at the
 * equator and 105 km at Batanes, so adding `radiusKm / 111` to both coordinates would draw an
 * ellipse that is too narrow in the north — and it would be narrowest exactly where the
 * archipelago's northern provinces are. The destination-point formula on a sphere costs nothing
 * here and is right everywhere.
 */

/** Mean Earth radius in kilometres, the value used throughout this project. */
const EARTH_RADIUS_KM = 6371.0088;

const DEGREES = 180 / Math.PI;
const RADIANS = Math.PI / 180;

/** A closed ring, typed structurally like the project's other map payloads. */
export interface RadiusRingFeature {
  readonly type: 'Feature';
  readonly properties: { readonly radiusKm: number };
  readonly geometry: {
    readonly type: 'Polygon';
    readonly coordinates: readonly (readonly [number, number])[][];
  };
}

/**
 * Builds a circle of the given radius around a point.
 *
 * @param vertices How many points to draw. 96 keeps the polygon smooth at city zoom, where a
 *   10 km circle fills the viewport and a coarser ring reads as a polygon rather than a circle.
 */
export function radiusRing(
  latitude: number,
  longitude: number,
  radiusKm: number,
  vertices = 96,
): RadiusRingFeature {
  const angular = radiusKm / EARTH_RADIUS_KM;
  const lat = latitude * RADIANS;
  const lon = longitude * RADIANS;

  const sinLat = Math.sin(lat);
  const cosLat = Math.cos(lat);
  const sinAngular = Math.sin(angular);
  const cosAngular = Math.cos(angular);

  const ring: [number, number][] = [];

  for (let step = 0; step <= vertices; step++) {
    // The last vertex repeats the first: GeoJSON requires a closed ring, and MapLibre
    // silently renders an unclosed one as a sliver.
    const bearing = ((step % vertices) / vertices) * 2 * Math.PI;

    const pointLat = Math.asin(sinLat * cosAngular + cosLat * sinAngular * Math.cos(bearing));

    const pointLon =
      lon +
      Math.atan2(
        Math.sin(bearing) * sinAngular * cosLat,
        cosAngular - sinLat * Math.sin(pointLat),
      );

    ring.push([normaliseLongitude(pointLon * DEGREES), pointLat * DEGREES]);
  }

  return {
    type: 'Feature',
    properties: { radiusKm },
    geometry: { type: 'Polygon', coordinates: [ring] },
  };
}

/**
 * Wraps a longitude into −180…180.
 *
 * Not theoretical for this archipelago: at a 300 km radius a circle around Batanes reaches
 * beyond 122°E, and one around the western Palawan coast approaches the projection's western
 * edge. An unwrapped value draws a line across the whole world.
 */
function normaliseLongitude(degrees: number): number {
  return ((degrees + 540) % 360) - 180;
}
