/**
 * API contract types.
 *
 * These mirror the response records in `Calametra.Application.Features`. Kept
 * hand-written rather than generated so the comments explaining *why* a field
 * exists survive on this side of the wire — the depth quality flag in
 * particular is meaningless to a client that does not know what it is for.
 */

/** A magnitude and the scale it was measured on. */
export interface MagnitudeReading {
  readonly value: number;

  /** Conventional label, e.g. `Mww`, `mb`, `Ms`. */
  readonly scale: string;

  /**
   * Scale family. Two magnitudes may only be compared numerically when their
   * families match: `mb` saturates around M6 and diverges from moment
   * magnitude, so a body-wave value and a moment value are different
   * quantities that happen to use similar numbers.
   */
  readonly scaleFamily: 'Unknown' | 'BodyWave' | 'SurfaceWave' | 'Local' | 'Moment';

  /** Pre-formatted for display, e.g. `Mww 6.5`. */
  readonly display: string;
}

/** A hypocentre depth and whether it was actually measured. */
export interface DepthReading {
  readonly kilometres: number | null;

  readonly quality: 'Unknown' | 'Constrained' | 'OperatorAssigned';

  /**
   * True only when the depth was resolved from observations.
   *
   * 36% of USGS Philippine events report a depth of exactly 10 km or 35 km,
   * which are NEIC defaults used when depth cannot be resolved. Plotting those
   * as measured values draws two false flat bands across the cross-section, so
   * any depth visualisation must branch on this flag.
   */
  readonly isMeasured: boolean;

  readonly display: string;
}

/** One agency's reading of an event. */
export interface ObservationSummary {
  readonly id: string;
  readonly agency: string;
  readonly datasetName: string;
  readonly sourceSlug: string;
  readonly externalId: string;
  readonly observedAt: string;
  readonly latitude: number;
  readonly longitude: number;
  readonly magnitude: MagnitudeReading | null;
  readonly depth: DepthReading;
  readonly isPreferred: boolean;
  readonly sourceUrl: string | null;
}

/** An earthquake as shown on the map, the timeline and in lists. */
export interface EarthquakeSummary {
  readonly id: string;
  readonly occurredAt: string;
  readonly latitude: number;
  readonly longitude: number;
  readonly magnitude: MagnitudeReading | null;
  readonly depth: DepthReading;

  /** Which agency the displayed figures came from. Never omitted. */
  readonly sourceAgency: string;
  readonly sourceSlug: string;

  /** More than one agency reported this event, so a comparison exists. */
  readonly hasMultipleObservations: boolean;

  /** Agencies disagree on magnitude, or reported on incomparable scales. */
  readonly hasMagnitudeDisagreement: boolean;
}

/** An earthquake with every agency's reading of it. */
export interface EarthquakeDetail {
  readonly id: string;
  readonly occurredAt: string;
  readonly latitude: number;
  readonly longitude: number;

  /**
   * Every reading, authoritative Philippine sources first.
   *
   * Plural by design. The same earthquake is measured differently by different
   * networks, so the client renders all of them rather than choosing one.
   */
  readonly observations: readonly ObservationSummary[];

  /** Agencies disagree beyond rounding, or reported on incomparable scales. */
  readonly hasMagnitudeDisagreement: boolean;

  /**
   * Plain-language reason the values differ, generated from the scales actually
   * present rather than a canned disclaimer. Null when there is nothing to explain.
   */
  readonly disagreementExplanation: string | null;
}

/** One month of activity, for the timeline histogram. */
export interface ActivityBucket {
  /** First instant of the month, UTC, ISO 8601. */
  readonly periodStart: string;

  readonly count: number;

  /**
   * Largest magnitude in the month, or null when none was reported. Lets the
   * histogram mark months containing a major event, which a count alone hides.
   */
  readonly maxMagnitude: number | null;
}

/** Monthly counts across the archive, plus the extent the timeline spans. */
export interface EarthquakeActivity {
  readonly firstEventAt: string | null;
  readonly lastEventAt: string | null;
  readonly totalCount: number;

  /** Highest single-month count, used to scale bar heights. */
  readonly peakMonthlyCount: number;

  /** One bucket per month, including months with no events. */
  readonly buckets: readonly ActivityBucket[];
}

/**
 * One event reduced to what a circle layer needs.
 *
 * Terse field names are deliberate — this arrives 27,000 at a time, where key names
 * are a measurable share of the payload. Measured: 2.3x smaller on the wire than the
 * search endpoint for the same events, in one request instead of 28.
 */
export interface MapPoint {
  /** Event id, for fetching detail on click. */
  readonly i: string;
  /** Latitude, degrees. */
  readonly y: number;
  /** Longitude, degrees. */
  readonly x: number;
  /** Magnitude, or null when none was reported. */
  readonly m: number | null;
  /** Magnitude scale, short form. */
  readonly s: string;
  /** Depth in kilometres, or null. */
  readonly d: number | null;
  /** Depth quality: 1 measured, 2 agency-assigned, 0 unknown. */
  readonly q: number;
  /** Origin time, Unix epoch milliseconds. */
  readonly t: number;
  /** More than one agency reported this event. */
  readonly n: boolean;
}

export interface MapDataResponse {
  readonly count: number;
  readonly points: readonly MapPoint[];
}

/** Depth quality codes as sent by the compact map endpoint. */
export const DEPTH_QUALITY = {
  unknown: 0,
  measured: 1,
  agencyAssigned: 2,
} as const;

export interface PaginatedList<T> {
  readonly items: readonly T[];
  readonly totalCount: number;
  readonly page: number;
  readonly pageSize: number;
  readonly totalPages: number;
  readonly hasPreviousPage: boolean;
  readonly hasNextPage: boolean;
}

/** Query parameters for the earthquake archive search. */
export interface EarthquakeQuery {
  readonly from?: string;
  readonly to?: string;
  readonly minMagnitude?: number;
  readonly maxMagnitude?: number;
  readonly minDepthKm?: number;
  readonly maxDepthKm?: number;
  readonly scaleFamily?: MagnitudeReading['scaleFamily'];

  /** Set false for depth analysis, to drop agency-assigned depths. */
  readonly includeAssignedDepths?: boolean;

  readonly centreLatitude?: number;
  readonly centreLongitude?: number;
  readonly radiusKm?: number;
  readonly page?: number;
  readonly pageSize?: number;
}

/** A displayable hazard layer, with its attribution and caveats. */
export interface HazardLayer {
  readonly id: string;
  readonly hazardType: string;
  readonly lens: 'Seismic' | 'Coastal' | 'Terrain' | 'Cyclone' | 'Exposure' | 'History';
  readonly displayName: string;

  /**
   * `RemoteWms` layers are proxied from the publishing agency and never stored
   * locally; `LocalVector` layers are served from Calametra's own database.
   */
  readonly deliveryMode: 'RemoteWms' | 'LocalVector';

  readonly supportsFeatureInfo: boolean;
  readonly isEnabledByDefault: boolean;
  readonly sortOrder: number;

  /** Plain-language explanation of what the layer shows. */
  readonly explainer: string | null;

  /** What a highlighted area does and does not imply. */
  readonly interpretationNote: string | null;

  readonly sourceAgency: string;
  readonly sourceDatasetName: string;
  readonly attribution: string;
  readonly sourceUrl: string | null;
  readonly termsUrl: string | null;
}

/** Official attributes of one hazard feature, verbatim from the publisher. */
export interface HazardFeatureAttributes {
  readonly layerName: string;
  readonly displayValue: string | null;
  readonly attributes: Readonly<Record<string, string>>;
}

/** RFC 9457 problem response, as returned by the API. */
export interface ProblemDetails {
  readonly title?: string;
  readonly detail?: string;
  readonly status?: number;

  /** Stable machine-readable key, e.g. `event.not_found`. */
  readonly code?: string;

  readonly errors?: Readonly<Record<string, readonly string[]>>;
  readonly traceId?: string;
}
