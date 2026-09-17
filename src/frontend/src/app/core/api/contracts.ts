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
   * The epicentre stated against the nearest city or municipality, e.g. `21 km NNW of Surigao
   * City, Province of Surigao del Norte`.
   *
   * Null when the nearest place is further than 300 km — 66 of the 27,241 catalogued events, all
   * deep-ocean — and when the place directory has not been imported. The distance is to the
   * gazetteer's representative point for the place, not to its boundary.
   */
  readonly location: string | null;

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

/** Compact canonical earthquake events authoritatively contained by one current COD-AB land outline. */
export interface LguContainedEarthquakeMapData {
  readonly canonicalPsgcCode: string;
  readonly name: string;
  readonly level: string;
  readonly boundaryGeometryAreaSquareKm: number;
  readonly count: number;
  readonly points: readonly MapPoint[];
  readonly countSemantics: string;
  readonly boundary: LguBoundaryEdition;
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

/** Storms whose best-track positions came within a place's radius. */
export interface CycloneProximity {
  readonly stormCount: number;
  readonly landfallCount: number;
  readonly strongestKnots: number | null;
  readonly strongestPeriod: string | null;
  readonly strongestAgency: string | null;
  readonly firstSeason: number | null;
  readonly lastSeason: number | null;
}

/** One decade of the storm record. */
export interface CycloneDecade {
  readonly decade: number;
  readonly stormCount: number;

  /** The more era-comparable series: coverage at sea depended on what could be observed. */
  readonly landfallCount: number;

  readonly namedInPhilippinesCount: number;
  readonly strongestKnots: number | null;
}

export interface CycloneDecades {
  readonly decades: readonly CycloneDecade[];

  /** The season from which JTWC rates its own best-track record as high quality. */
  readonly reliableFromSeason: number;

  readonly totalCount: number;
}

/** One agency's fix inside a nearby-track segment. */
export interface NearbyCycloneFix {
  readonly capturedAt: string;
  readonly latitude: number;
  readonly longitude: number;
  /** Null when this agency reported a position but no wind. */
  readonly windKnots: number | null;
  readonly isLandfall: boolean;
}

/**
 * The part of one storm's track that passed near a point.
 *
 * Clipped, not whole: see `GET /api/cyclones/nearby`. `peakKnotsNearby` is the strongest reading
 * within this segment, with the averaging period that produced it — not the storm's peak intensity.
 */
export interface NearbyCycloneTrack {
  readonly eventId: string;
  readonly name: string | null;
  readonly localName: string | null;
  readonly season: number;
  readonly agency: string;
  readonly averagingPeriod: string;
  readonly peakKnotsNearby: number | null;
  /** Upper bound: best-track fixes are three- or six-hourly, so the true minimum falls between two. */
  readonly closestApproachKm: number;
  /** Across every agency's fixes inside the radius, so it agrees with the place context's count. */
  readonly landfallNearby: boolean;
  readonly fixes: readonly NearbyCycloneFix[];
}

export interface NearbyCycloneTracks {
  readonly tracks: readonly NearbyCycloneTrack[];
  /** Storms within the radius. Exceeds `tracks.length` when the limit bit. */
  readonly stormCount: number;
  readonly fixCount: number;
  readonly radiusKm: number;
  /** The wider radius the segments are drawn to, so a track can visibly enter and leave. */
  readonly drawnRadiusKm: number;
  readonly reliableFromSeason: number;
}

/** One decade of the catalogue's own history. */
export interface DecadeSummary {
  readonly decade: number;
  readonly totalCount: number;
  readonly comparableCount: number;

  /** The era-comparable rate. This is the series to read across decades; the total is not. */
  readonly comparablePerYear: number;

  readonly assignedDepthCount: number;
  readonly assignedDepthShare: number;
  readonly dominantScale: string | null;
  readonly strongestMagnitude: number | null;
  readonly strongestScale: string | null;
}

/** How complete the catalogue is, decade by decade. */
export interface CatalogueCompleteness {
  readonly decades: readonly DecadeSummary[];
  readonly comparableMagnitudeFloor: number;
  readonly totalCount: number;
  readonly observedAt: string;
}

/** Query parameters for the earthquake archive search. */export interface EarthquakeQuery {
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

  /**
   * How the page is ordered.
   *
   * `Strongest` requires `scaleFamily`: the archive is 92.8% body-wave with nearly every large event
   * reported as moment magnitude, so ranking a mixed list by magnitude value would order events on a
   * difference that belongs to the scale rather than to the earthquake. The server refuses the
   * combination rather than serving it.
   */
  readonly sort?: 'Newest' | 'Oldest' | 'Strongest';
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

  /**
   * Whether the publisher exposes a pre-rendered tile cache for this layer.
   *
   * When true the map addresses tiles as `{z}/{x}/{y}` at 256 px; when false it asks the
   * publisher to render a bounding box. Not a preference: the MGB susceptibility maps serve a
   * cached tile in 60–120 ms and render the same tile in about 19 seconds.
   */
  readonly supportsCachedTiles: boolean;
  readonly isEnabledByDefault: boolean;
  readonly sortOrder: number;

  /**
   * Zoom below which this layer must not be requested, or null for no limit.
   *
   * A measured property of the publisher's service, not a styling choice: PHIVOLCS renders every tile
   * on demand, and the same layer costs 2.9 s at 400 km and 19.1 s across a national view. Enforced
   * here because a bounding-box request carries no zoom, so only the client can tell the difference.
   */
  readonly minimumZoom: number | null;

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

/** Properties on a stored hazard feature. */
export interface HazardFeatureProperties {
  readonly id: string;
  readonly externalId: string;
  readonly name: string | null;

  /** Publisher's own classification, e.g. a fault slip type such as `Sinistral`. */
  readonly classification: string | null;
}

/**
 * Stored hazard geometry as GeoJSON, with the attribution that must accompany it.
 *
 * Only returned for layers Calametra is licensed to store. Proxied layers return no
 * features and are rendered through the tile endpoint instead.
 */
export interface HazardFeatureCollection {
  readonly type: 'FeatureCollection';
  readonly features: readonly {
    readonly type: 'Feature';
    readonly properties: HazardFeatureProperties;
    readonly geometry: {
      readonly type: string;
      readonly coordinates: unknown;
    };
  }[];
  readonly attribution: string;
  readonly termsUrl: string | null;
}

/** RFC 9457 problem response, as returned by the API. */
/**
 * A request for a vertical slice through the crust.
 *
 * Note that `includeAssignedDepths` defaults to FALSE here, the opposite of the map's
 * default. On a map an agency-assigned depth still tells you an earthquake happened
 * there; on a depth section it is a fabricated vertical position, and 43% of the archive
 * carries one. See {@link CrossSection.excludedAssignedDepthCount}.
 */
export interface CrossSectionQuery {
  readonly startLatitude: number;
  readonly startLongitude: number;
  readonly endLatitude: number;
  readonly endLongitude: number;
  /** Half-width either side of the line, in kilometres. */
  readonly corridorKm?: number;
  readonly includeAssignedDepths?: boolean;
  readonly minMagnitude?: number;
  readonly from?: string;
  readonly to?: string;
}

/** One hypocentre positioned on a section. */
export interface CrossSectionPoint {
  readonly eventId: string;
  /** Distance from the section's start point. The horizontal axis. */
  readonly alongKm: number;
  /** Hypocentre depth. The vertical axis, drawn downward. */
  readonly depthKm: number;
  /**
   * Perpendicular distance from the line.
   *
   * Reported so the plot can fade events gathered at the corridor's edge rather than
   * implying they sit exactly where they are drawn.
   */
  readonly offsetKm: number;
  readonly magnitude: number | null;
  /** Never omitted: a magnitude without its scale is not comparable. */
  readonly magnitudeScale: string;
  /** False when the agency assigned the depth rather than measuring it. */
  readonly depthMeasured: boolean;
  readonly occurredAt: string;
}

/** A section, its extent, and the hypocentres on it. */
export interface CrossSection {
  readonly lengthKm: number;
  readonly corridorKm: number;
  /** Rounded up to a sensible axis maximum, so shallow sections are not squashed. */
  readonly maxDepthKm: number;
  readonly totalCount: number;
  /**
   * How many events were set aside because their depth was assigned, not measured.
   *
   * Surfaced in the UI rather than dropped silently. On a trench-crossing profile this
   * regularly exceeds the number of points actually plotted.
   */
  readonly excludedAssignedDepthCount: number;
  readonly points: readonly CrossSectionPoint[];
}

/**
 * One earthquake resembling a reference event.
 *
 * The component deltas are nullable, and null means "this comparison could not be made"
 * rather than "no difference". `magnitudeDelta` is null across scale families, and
 * `depthDeltaKm` is null when either depth was assigned rather than measured. Rendering
 * either as 0 would assert an agreement the data does not support.
 */
export interface SimilarEarthquake {
  readonly eventId: string;
  readonly occurredAt: string;
  readonly latitude: number;
  readonly longitude: number;
  /** Nearest city or municipality, or null beyond 300 km. See {@link EarthquakeDetail.location}. */
  readonly location: string | null;
  readonly agency: string;
  readonly magnitudeDisplay: string;
  readonly depthDisplay: string;
  readonly distanceKm: number;
  readonly magnitudeDelta: number | null;
  readonly depthDeltaKm: number | null;
  /** False when the two readings use scale families that cannot be compared. */
  readonly magnitudeComparable: boolean;
  /** 0–1 composite. Reproducible by hand from the components. */
  readonly score: number;
  /** One statement per component, including the ones that could not be computed. */
  readonly explanations: readonly string[];
}

/**
 * Similarity results, with the counts needed to explain a short list.
 *
 * The three counts matter as much as the matches. For the 2017 Surigao event, 3,612 of
 * 3,633 nearby earthquakes report a magnitude that cannot be compared with PHIVOLCS's
 * Ms 6.7, so the strict defaults return exactly one match. Presented bare that looks like
 * a broken feature; presented with the counts it is the platform's central point.
 */
export interface SimilarEarthquakes {
  readonly referenceEventId: string;
  readonly referenceMagnitude: string;
  readonly referenceDepth: string;
  /** Within the distance tolerance, before any quality filter. The baseline. */
  readonly nearbyEvents: number;
  /** Of the nearby events, how many use an incomparable magnitude scale. */
  readonly excludedForIncomparableScale: number;
  /** Of the nearby events, how many carry an assigned depth. Overlaps with the above. */
  readonly excludedForAssignedDepth: number;
  /** What survived the filters and was scored. */
  readonly candidatesConsidered: number;
  /** Admitted despite an incomparable scale, when that filter is relaxed. */
  readonly admittedWithIncomparableMagnitude: number;
  readonly matches: readonly SimilarEarthquake[];
}

export interface SimilarEarthquakesQuery {
  /** Capped at 300 km server-side: beyond that the spatial index stops being used. */
  readonly maxDistanceKm?: number;
  readonly maxMagnitudeDelta?: number;
  readonly maxDepthDeltaKm?: number;
  readonly requireComparableMagnitudeScale?: boolean;
  readonly excludeOperatorAssignedDepths?: boolean;
  readonly limit?: number;
}

/**
 * The three cross-event differences.
 *
 * Two of them are nullable, and null means the comparison could not be made rather than
 * that there is no difference. Each carries a note explaining why in either case, so the
 * UI never has to compose that reasoning itself.
 */
export interface ComparisonDeltas {
  readonly separationKm: number;
  /** Human phrasing — "2 years apart", "4 hours apart". */
  readonly timeApart: string;
  readonly daysApart: number;
  /** Null when the two preferred readings use incomparable scale families. */
  readonly magnitudeDelta: number | null;
  readonly magnitudeNote: string;
  /** Null when either depth was assigned rather than measured. */
  readonly depthDelta: number | null;
  readonly depthNote: string;
}

/** Two earthquakes with their full reading sets and the derived differences. */
export interface EarthquakeComparison {
  readonly left: EarthquakeDetail;
  readonly right: EarthquakeDetail;
  readonly deltas: ComparisonDeltas;
}

/**
 * A storm's peak intensity for one wind averaging period.
 *
 * There is one of these per period, never one per storm. Agencies average sustained wind over
 * different intervals — one minute for JTWC, ten for JMA and Hong Kong, two for CMA — and a
 * shorter interval preserves brief peaks a longer one smooths away. Reducing them to a single
 * "peak wind" would silently report whichever agency uses the shortest interval.
 */
export interface PeakIntensity {
  readonly knots: number;
  readonly kilometresPerHour: number;
  /** The averaging interval, without which the figure is not comparable. */
  readonly period: string;
  readonly agency: string;
}

/**
 * How a cyclone list is ordered.
 *
 * `Intensity` is the default and orders by lowest central pressure — the one intensity measure
 * comparable across agencies. Recency puts provisional single-agency records first, which is the
 * thin end of the archive.
 */
export type CycloneOrder = 'Intensity' | 'Recent';

export interface CycloneSummary {
  readonly id: string;
  /**
   * International name, or null for a storm that never earned one.
   *
   * Philippine storms also carry a PAGASA local name — Haiyan was Yolanda — which the upstream
   * archive does not hold.
   */
  readonly name: string | null;
  /**
   * The PAGASA name, where a mapping is held.
   *
   * A separate field rather than folded into `name`, because the two come from different naming
   * authorities — JMA assigns the international name, PAGASA the local one. For a Philippine
   * reader the local name is usually the recognisable one and often the only one in memory.
   *
   * Null is common: the crosswalk is curated rather than ingested, since PAGASA publishes name
   * lists as documents and no machine-readable international-to-local mapping exists.
   */
  readonly localName: string | null;
  readonly season: number;
  readonly startedAt: string;
  readonly endedAt: string;
  /** Needs no averaging qualifier: pressure is the same quantity to every agency. */
  readonly minimumPressureMillibars: number | null;
  /** How many agencies analysed this storm. Recent seasons often carry only one. */
  readonly agencyCount: number;
  readonly fixCount: number;
  readonly madeLandfall: boolean;
  readonly peaks: readonly PeakIntensity[];
}

/** One agency's fix on the centre at one moment. */
export interface CycloneFix {
  readonly capturedAt: string;
  readonly latitude: number;
  readonly longitude: number;
  /** Null when this agency reported pressure but no wind. */
  readonly windKnots: number | null;
  readonly pressureMillibars: number | null;
  readonly classification: string | null;
  readonly distanceToLandKm: number | null;
  /** True when the storm crosses the coast between this fix and the next. */
  readonly isLandfall: boolean;

  /**
   * Radius of maximum wind in nautical miles — the eyewall.
   *
   * The only figure that lets a cyclone be drawn at its real scale. JTWC publishes it; JMA and
   * KMA do not, so an ellipse-geometry fix has no eye radius.
   */
  readonly eyewallRadiusNm: number | null;

  /** Radius of the outermost closed isobar in nautical miles. */
  readonly outerRadiusNm: number | null;

  /**
   * How this agency describes the gale field: `Quadrants`, `Ellipse` or `None`.
   *
   * The client must branch rather than assume one shape. JTWC gives four directional radii; JMA
   * and KMA give an ellipse. CMA and HKO give neither.
   */
  readonly galeGeometry: 'Quadrants' | 'Ellipse' | 'None';

  /**
   * The wind speed the gale radii are measured at — 34 kt for JTWC, 30 kt for JMA and KMA.
   *
   * Shown wherever a radius is shown. A radius without its threshold looks comparable across
   * agencies and is not.
   */
  readonly galeThresholdKnots: number;

  readonly galeNorthEastNm: number | null;
  readonly galeSouthEastNm: number | null;
  readonly galeSouthWestNm: number | null;
  readonly galeNorthWestNm: number | null;
  readonly galeLongAxisNm: number | null;
  readonly galeShortAxisNm: number | null;
  readonly galeBearingDegrees: number | null;

  /**
   * Widest quadrant divided by narrowest, where all four are reported.
   *
   * Measured values in this archive reach 37, which is why the field is never drawn as a circle.
   */
  readonly galeAsymmetryRatio: number | null;

  /**
   * Storm-force (50 kt) and hurricane-force (64 kt) radii per quadrant.
   *
   * Together with the 34 kt gale radius these form the nested bands used in tropical-cyclone
   * wind-field products. JTWC is the only agency in this basin that publishes them, so an
   * ellipse-geometry fix has none.
   */
  readonly stormNorthEastNm: number | null;
  readonly stormSouthEastNm: number | null;
  readonly stormSouthWestNm: number | null;
  readonly stormNorthWestNm: number | null;
  readonly hurricaneNorthEastNm: number | null;
  readonly hurricaneSouthEastNm: number | null;
  readonly hurricaneSouthWestNm: number | null;
  readonly hurricaneNorthWestNm: number | null;
}

/** One agency's track. */
export interface AgencyTrack {
  readonly agency: string;
  readonly sourceSlug: string;
  /** Stated once per track: it is a property of the agency's method, not of a fix. */
  readonly averagingPeriod: string;
  readonly peakWindKnots: number | null;
  readonly minimumPressureMillibars: number | null;
  readonly fixes: readonly CycloneFix[];
}

/** A storm with one track per agency, never an averaged path. */
export interface CycloneTrack {
  readonly id: string;
  readonly name: string | null;
  /** The PAGASA name, where a mapping is held. See {@link CycloneSummary.localName}. */
  readonly localName: string | null;
  readonly season: number;
  readonly startedAt: string;
  readonly endedAt: string;
  readonly madeLandfall: boolean;
  readonly tracks: readonly AgencyTrack[];
  /**
   * Plain-language statement of how far the agencies diverge, generated server-side from the
   * readings present. Null when fewer than two agencies reported wind.
   */
  readonly agreementNote: string | null;
}

/**
 * One administrative place matching a name search.
 *
 * `containedBy` is not decoration: 111 of the country's city and municipality names are not
 * unique, so a bare name is genuinely ambiguous and the province is what resolves it.
 */
export interface PlaceMatch {
  /**
   * The durable identifier — the pre-2019 nine-digit PSGC — and null for five places in the
   * directory. Anything shared or cited uses this, never an internal id.
   */
  readonly psgcCode: string | null;
  readonly name: string;
  /** `Region`, `Province`, `City` or `Municipality`. */
  readonly kind: string;
  readonly containedBy: string | null;
  readonly region: string | null;
  readonly latitude: number;
  readonly longitude: number;
}

/** One earthquake near a place. */
export interface PlaceEvent {
  readonly eventId: string;
  readonly occurredAt: string;
  readonly latitude: number;
  readonly longitude: number;
  readonly magnitudeDisplay: string;
  readonly depthDisplay: string;
  readonly agency: string;
  readonly distanceKm: number;
}

/**
 * The strongest reading within one scale family.
 *
 * Returned per family rather than as a single "largest nearby earthquake", because body-wave
 * magnitude saturates near M6 and cannot be ranked against a moment magnitude. Same refusal as
 * cyclone peak intensity per averaging period.
 */
export interface PlaceStrongestReading {
  readonly scaleFamily: string;
  readonly readingsInFamily: number;
  readonly event: PlaceEvent;
}

/** A mapped fault trace near a place. Proximity, never attribution. */
export interface PlaceFault {
  readonly name: string;
  readonly classification: string | null;
  readonly distanceKm: number;
  readonly withinRadius: boolean;
  readonly agency: string;
  readonly attribution: string;
}

/** What the archive holds around one place. */
export interface PlaceContext {
  readonly psgcCode: string | null;
  readonly name: string;
  readonly kind: string;
  readonly containedBy: string | null;
  readonly region: string | null;
  readonly latitude: number;
  readonly longitude: number;
  readonly radiusKm: number;
  readonly eventsWithinRadius: number;
  /** Of those, how many are M6.0+ — the only subset comparable between eras or places. */
  readonly eventsAtComparableMagnitude: number;
  readonly eventsWithAssignedDepth: number;
  readonly earliestEvent: string | null;
  readonly latestEvent: string | null;
  readonly strongestByScaleFamily: readonly PlaceStrongestReading[];
  readonly mostRecentEvent: PlaceEvent | null;
  readonly nearestFaults: readonly PlaceFault[];

  /**
   * Storms whose track passed within the radius.
   *
   * Track proximity, not impact: a cyclone's damaging winds and rain reach far beyond the positions its
   * analysts recorded, so this undercounts exposure.
   */
  readonly cyclones: CycloneProximity;
  /** Server-composed caveats. Rendered verbatim: they are claims about the data. */
  readonly notes: readonly string[];
}

/** The dated COD-AB boundary edition used for an LGU containment result. */
export interface LguBoundaryEdition {
  readonly label: string;
  readonly vintage: string | null;
  readonly attribution: string;
}

/** A published statistical area, versioned independently from mapped boundary geometry. */
export interface LguOfficialLandArea {
  readonly squareKm: number;
  readonly basis: 'Unspecified' | 'CadastralSurvey' | 'Estimated';
  readonly editionLabel: string;
  readonly referenceYear: number;
  readonly matrixId: string;
  readonly sourceUpdatedAt: string | null;
  readonly provenanceLabel: string;
  readonly attribution: string;
}

/** Canonical PSGC identity and its current official-area observation, if one exists. */
export interface AdministrativeUnit {
  readonly canonicalPsgcCode: string;
  readonly name: string;
  readonly level: string;
  readonly registerEdition: string;
  readonly officialLandArea: LguOfficialLandArea | null;
}

/**
 * Earthquakes whose canonical epicentres fall within/on one current LGU land boundary.
 *
 * This is deliberately hazard-specific. It is neither the place-radius answer nor a generic hazard
 * total, and `earthquakeCount` counts canonical events rather than per-agency observation rows.
 */
export interface LguEarthquakeContainment {
  readonly canonicalPsgcCode: string;
  readonly name: string;
  readonly level: string;
  readonly boundaryGeometryAreaSquareKm: number;
  /** @deprecated Compatibility alias; this is not an official/statistical LGU land area. */
  readonly landAreaSquareKm: number;
  readonly earthquakeCount: number;

  /**
   * Diagnostic implementation metadata. Callers must not branch on its literal value; the durable
   * contract is `countSemantics`, including exact-boundary epicentres.
   */
  readonly spatialPredicate: string;
  readonly countSemantics: string;
  readonly boundary: LguBoundaryEdition;
}

/**
 * One upstream dataset, as the platform records it.
 *
 * The credits page is rendered from these rather than from a hand-written list, so it cannot
 * drift from what the system actually reads.
 */
export interface DataSourceCredit {
  readonly slug: string;
  readonly agency: string;
  readonly datasetName: string;
  /** How the data is obtained: `RestApi`, `BulkFile`, `WmsProxy`, `ManualImport`… */
  readonly accessKind: string;
  readonly attribution: string;
  readonly sourceUrl: string | null;
  readonly termsUrl: string | null;
  /** Whether this platform may store the data, or may only proxy the publisher's rendering. */
  readonly isRedistributable: boolean;
  /** Whether this is an official Philippine authority for its hazard domain. */
  readonly isAuthoritativeForPhilippines: boolean;
  readonly minimumReliableMagnitude: number | null;
  readonly coverageNotes: string | null;
  /** Null means registered but never yet read. */
  readonly lastRetrievedAt: string | null;
}

export interface ProblemDetails {
  readonly title?: string;
  readonly detail?: string;
  readonly status?: number;

  /** Stable machine-readable key, e.g. `event.not_found`. */
  readonly code?: string;

  readonly errors?: Readonly<Record<string, readonly string[]>>;
  readonly traceId?: string;
}
