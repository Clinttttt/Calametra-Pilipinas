import { DEPTH_BANDS, UNMEASURED_DEPTH_COLOUR } from './depth-scale';
import { DEPTH_QUALITY, type MapPoint } from '../api/contracts';

/**
 * Properties carried on each map feature.
 *
 * Deliberately flat and primitive. MapLibre paint and filter expressions can only
 * read scalar feature properties, so anything the styling or filtering depends on —
 * the depth band, whether the depth was measured, the origin time as a number — is
 * precomputed here rather than derived on the GPU.
 */
export interface EarthquakeFeatureProperties {
  readonly id: string;
  readonly magnitude: number | null;
  readonly magnitudeDisplay: string;
  readonly depthKm: number | null;
  readonly depthDisplay: string;

  /** Band id, consumed by the depth colour paint expression. */
  readonly depthBand: string;

  /**
   * False when the reporting agency fixed the depth to a default rather than
   * measuring it. Drives both the colour and the disclosure ring, so it must travel
   * with every feature. 43% of the archive carries a non-measured depth.
   */
  readonly depthMeasured: boolean;

  /**
   * Origin time as epoch milliseconds.
   *
   * Numeric because MapLibre filter expressions cannot parse or compare ISO date
   * strings. The timeline filters ~27,000 features by date on every frame of
   * playback, and a numeric comparison is the only form evaluable on the GPU.
   */
  readonly epochMs: number;

  readonly hasMultipleObservations: boolean;
}

export interface EarthquakeGeoJson {
  readonly type: 'FeatureCollection';
  readonly features: readonly {
    readonly type: 'Feature';
    readonly id: number;
    readonly properties: EarthquakeFeatureProperties;
    readonly geometry: { readonly type: 'Point'; readonly coordinates: readonly [number, number] };
  }[];
}

/**
 * Converts the compact map payload into a GeoJSON FeatureCollection for MapLibre.
 *
 * A single GeoJSON source rather than one marker element per event. At ~27,000
 * events, DOM markers would mean 27,000 absolutely-positioned elements repositioned
 * on every frame of a pan; a GeoJSON source is drawn by the GPU in one pass.
 *
 * The numeric `id` is required for MapLibre feature state, which is what allows a
 * hovered or selected event to be styled without rebuilding the whole source.
 */
export function toEarthquakeGeoJson(points: readonly MapPoint[]): EarthquakeGeoJson {
  return {
    type: 'FeatureCollection',
    features: points.map((point, index) => {
      const measured = point.q === DEPTH_QUALITY.measured;

      return {
        type: 'Feature' as const,
        // Sequential rather than derived from the GUID: feature state ids must be
        // integers, and a GUID has no integer form.
        id: index,
        properties: {
          id: point.i,
          magnitude: point.m,
          magnitudeDisplay: point.m === null ? 'Magnitude unknown' : `${point.s} ${point.m.toFixed(1)}`,
          depthKm: point.d,
          depthDisplay: describeDepth(point.d, point.q),
          depthBand: bandFor(point.d, measured),
          depthMeasured: measured,
          epochMs: point.t,
          hasMultipleObservations: point.n,
        },
        geometry: {
          type: 'Point' as const,
          // GeoJSON order: longitude first.
          coordinates: [point.x, point.y] as const,
        },
      };
    }),
  };
}

/** Band id for a depth, or `unknown` when it was not measured. */
function bandFor(depthKm: number | null, measured: boolean): string {
  if (!measured || depthKm === null) {
    return 'unknown';
  }

  const band = DEPTH_BANDS.find(
    (candidate) => depthKm >= candidate.fromKm && (candidate.toKm === null || depthKm < candidate.toKm),
  );

  return band?.id ?? 'unknown';
}

/**
 * Human-readable depth, marking agency-assigned values.
 *
 * An assigned depth is never shown as a bare number: it would read as a measurement.
 */
function describeDepth(depthKm: number | null, quality: number): string {
  if (depthKm === null) {
    return 'Depth unknown';
  }

  return quality === DEPTH_QUALITY.agencyAssigned
    ? `${depthKm.toFixed(1)} km (assigned)`
    : `${depthKm.toFixed(1)} km`;
}

/** Re-exported so the map layer and the legend agree on the unmeasured colour. */
export { UNMEASURED_DEPTH_COLOUR };
