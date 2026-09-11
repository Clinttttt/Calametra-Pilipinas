import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { type Observable } from 'rxjs';

import {
  type CycloneOrder,
  type CycloneSummary,
  type CycloneTrack,
  type DataSourceCredit,
  type EarthquakeActivity,
  type EarthquakeComparison,
  type EarthquakeDetail,
  type EarthquakeQuery,
  type EarthquakeSummary,
  type CrossSection,
  type CrossSectionQuery,
  type HazardFeatureAttributes,
  type HazardFeatureCollection,
  type HazardLayer,
  type MapDataResponse,
  type PaginatedList,
  type PlaceContext,
  type PlaceMatch,
  type SimilarEarthquakes,
  type SimilarEarthquakesQuery,
} from './contracts';

/**
 * Typed access to the Calametra API.
 *
 * URLs are root-relative; `apiBaseUrlInterceptor` resolves them against the
 * configured host. Parameters are assembled here rather than by callers so that
 * `undefined` is consistently omitted instead of being serialised as the string
 * `"undefined"` — a defect that surfaces as a confusing 400 from the API.
 */
@Injectable({ providedIn: 'root' })
export class CalametraApi {
  private readonly http = inject(HttpClient);

  /** Searches the earthquake archive. */
  searchEarthquakes(query: EarthquakeQuery): Observable<PaginatedList<EarthquakeSummary>> {
    return this.http.get<PaginatedList<EarthquakeSummary>>('/api/earthquakes', {
      params: toHttpParams(query),
    });
  }

  /**
   * Fetches one earthquake with every agency's reading.
   *
   * Separate from the list endpoint because a list can only carry one reading per
   * row. This is what the detail panel uses to show, for example, PHIVOLCS Ms 6.7
   * beside USGS Mww 6.5 for the 2017 Surigao event.
   */
  getEarthquakeDetail(eventId: string): Observable<EarthquakeDetail> {
    return this.http.get<EarthquakeDetail>(`/api/earthquakes/${eventId}`);
  }

  /**
   * Fetches one earthquake by the reporting agency's own identifier.
   *
   * The durable way to reference an event. This platform's own ids are minted at insert, so
   * they change whenever the archive is re-ingested — which happens whenever a source is
   * refreshed. An agency identifier such as `us20008ixa` or `iscgem913230` survives that,
   * is citable in a publication, and resolves to the same event in any copy of the catalogue.
   *
   * Anything durable must use this: curated story content, a bookmark, a shared link. Use
   * {@link getEarthquakeDetail} only for an id obtained during the current session, such as
   * from a map click.
   */
  getEarthquakeByAgencyId(agencyEventId: string): Observable<EarthquakeDetail> {
    return this.http.get<EarthquakeDetail>(
      `/api/earthquakes/external/${encodeURIComponent(agencyEventId)}`,
    );
  }

  /**
   * The whole archive, compactly, for map rendering.
   *
   * One request rather than paging the search endpoint: measured at 2.3x smaller on
   * the wire and 27 fewer round trips for the same events, because it omits the
   * agency names and formatted strings the map never reads.
   */
  getEarthquakeMapData(): Observable<MapDataResponse> {
    return this.http.get<MapDataResponse>('/api/earthquakes/map');
  }

  /**
   * Monthly event counts for the timeline's density histogram.
   *
   * Aggregated server-side rather than counted from the events the client holds:
   * the client only has what it fetched, so counting locally would produce a
   * histogram that disagrees with the archive.
   */
  getEarthquakeActivity(): Observable<EarthquakeActivity> {
    return this.http.get<EarthquakeActivity>('/api/earthquakes/activity');
  }

  /**
   * Hypocentres along a vertical slice through the crust.
   *
   * The signature depth view: a map cannot show how deep earthquakes are, and across the
   * Philippine trenches a section reveals the subducting slab as a dipping plane of
   * seismicity reaching 667 km.
   *
   * Agency-assigned depths are excluded unless asked for. Measured on a profile across
   * Mindanao, 1,520 assigned depths were set aside against 1,237 real ones — plotting
   * them would draw four flat bands through more than half the section, and they would
   * look like genuine structure.
   */
  getCrossSection(query: CrossSectionQuery): Observable<CrossSection> {
    return this.http.get<CrossSection>('/api/earthquakes/cross-section', {
      params: toHttpParams(query),
    });
  }

  /**
   * Earthquakes resembling a given one, ranked with the reasoning attached.
   *
   * Returns a component breakdown per match rather than a bare score, because two of the
   * three comparisons are frequently unavailable: magnitude difference is null across
   * scale families, and depth difference is null when either depth was assigned rather
   * than measured.
   *
   * Expect short lists under the defaults. For the 2017 Surigao event 3,612 of 3,633
   * nearby earthquakes report an incomparable magnitude scale, so exactly one match
   * survives — which is why the response carries the exclusion counts.
   */
  getSimilarEarthquakes(
    eventId: string,
    query: SimilarEarthquakesQuery = {},
  ): Observable<SimilarEarthquakes> {
    return this.http.get<SimilarEarthquakes>(`/api/earthquakes/${eventId}/similar`, {
      params: toHttpParams(query),
    });
  }

  /**
   * Two earthquakes set against each other.
   *
   * The deltas are computed server-side rather than in the client, because two of the three
   * depend on domain rules — whether two magnitude scales belong to comparable families, and
   * whether a depth was measured — and reimplementing those in TypeScript would put the
   * platform's central claims in two places.
   */
  compareEarthquakes(leftEventId: string, rightEventId: string): Observable<EarthquakeComparison> {
    return this.http.get<EarthquakeComparison>('/api/earthquakes/compare', {
      params: toHttpParams({ left: leftEventId, right: rightEventId }),
    });
  }

  /**
   * Tropical cyclones that entered the Philippine area.
   *
   * Peak intensity arrives once per averaging period rather than as a single figure, so a
   * caller cannot accidentally present a one-minute mean as equivalent to a ten-minute one.
   */
  searchCyclones(
    season?: number,
    landfallOnly = false,
    order: CycloneOrder = 'Intensity',
    name?: string,
  ): Observable<readonly CycloneSummary[]> {
    return this.http.get<readonly CycloneSummary[]>('/api/cyclones', {
      params: toHttpParams({ season, landfallOnly, order, name }),
    });
  }

  /**
   * One storm's track, as each agency drew it.
   *
   * Returns one track per agency rather than an averaged path: agencies differ on where the
   * centre was as well as how strong it was, and a mean track would be a line no agency
   * published.
   */
  getCycloneTrack(eventId: string): Observable<CycloneTrack> {
    return this.http.get<CycloneTrack>(`/api/cyclones/${eventId}`);
  }

  /**
   * One storm's track, by the IBTrACS storm identifier.
   *
   * The cyclone counterpart of {@link getEarthquakeByAgencyId}, and durable for the same
   * reason: the IBTrACS import is a re-runnable one-shot, so a storm's internal id changes
   * whenever the basin file is re-imported.
   *
   * The SID — `2013306N07162` for Haiyan — rather than name and season, because international
   * names are reused: this archive holds MERANTI in 2010 and 2016, GONI in 2015 and 2020.
   */
  getCycloneByStormId(externalStormId: string): Observable<CycloneTrack> {
    return this.http.get<CycloneTrack>(
      `/api/cyclones/external/${encodeURIComponent(externalStormId)}`,
    );
  }

  /** Returns the hazard layer catalogue, with attribution and explainers. */
  listHazardLayers(lens?: HazardLayer['lens']): Observable<readonly HazardLayer[]> {
    return this.http.get<readonly HazardLayer[]>('/api/hazard-layers', {
      params: toHttpParams({ lens }),
    });
  }

  /**
   * Stored geometry for a layer Calametra is licensed to hold.
   *
   * Returns an empty collection for proxied layers — check `deliveryMode` on the
   * catalogue entry rather than inferring from an empty result.
   */
  getHazardFeatures(layerId: string): Observable<HazardFeatureCollection> {
    return this.http.get<HazardFeatureCollection>(`/api/hazard-layers/${layerId}/features`);
  }

  /**
   * Builds the raster tile template for a proxied hazard layer.
   *
   * Returns a URL template rather than fetching anything: MapLibre performs the
   * requests itself, and `{bbox-epsg-3857}` is substituted by the renderer per
   * tile. This is why the API accepts EPSG:3857 despite the upstream service
   * advertising only EPSG:4326 — MapLibre raster sources speak Web Mercator.
   */
  hazardTileTemplate(baseUrl: string, layerId: string, tileSize = 256): string {
    return (
      `${baseUrl}/api/hazard-layers/${layerId}/tile` +
      `?bbox={bbox-epsg-3857}&width=${tileSize}&height=${tileSize}`
    );
  }

  /**
   * Builds the tile template for a layer whose publisher exposes a pre-rendered cache.
   *
   * Preferred wherever `supportsCachedTiles` is set on the catalogue entry. For the DOST-MGB
   * susceptibility maps the same tile takes 60–120 ms from the agency's cache against 18.8–19.4
   * seconds rendered through WMS — the difference between a layer a reader can pan and one that
   * appears to hang. It is also considerably kinder to the agency's server.
   */
  cachedHazardTileTemplate(baseUrl: string, layerId: string): string {
    return `${baseUrl}/api/hazard-layers/${layerId}/tile/{z}/{x}/{y}`;
  }

  /** Fetches the publisher's own attributes for the feature at a position. */
  identifyHazardFeature(
    layerId: string,
    latitude: number,
    longitude: number,
  ): Observable<readonly HazardFeatureAttributes[]> {
    return this.http.get<readonly HazardFeatureAttributes[]>(
      `/api/hazard-layers/${layerId}/identify`,
      { params: toHttpParams({ latitude, longitude }) },
    );
  }

  /**
   * Finds administrative places by name.
   *
   * Cities, municipalities, provinces and regions — 1,750 in all. Barangays are not held.
   */
  searchPlaces(term: string, limit = 20): Observable<readonly PlaceMatch[]> {
    return this.http.get<readonly PlaceMatch[]>('/api/places', {
      params: toHttpParams({ q: term, limit }),
    });
  }

  /**
   * Fetches what the archive holds around one place.
   *
   * Keyed on the PSGC code rather than an internal id, so a link to a place survives the
   * directory being re-imported — the same rule as {@link getEarthquakeByAgencyId}.
   */
  getPlaceContext(psgcCode: string, radiusKm: number): Observable<PlaceContext> {
    return this.http.get<PlaceContext>(
      `/api/places/${encodeURIComponent(psgcCode)}/context`,
      { params: toHttpParams({ radiusKm }) },
    );
  }

  /**
   * Every dataset this platform reads, with its licence position and known limits.
   *
   * The credits page is built from this so attribution cannot drift from what is actually read.
   * A source registered here may hold no rows: reference data is seeded before ingestion runs,
   * and a proxied source stores nothing by design.
   */
  listDataSources(): Observable<readonly DataSourceCredit[]> {
    return this.http.get<readonly DataSourceCredit[]>('/api/data-sources');
  }
}

/**
 * Serialises a partial query object, omitting anything unset.
 *
 * Takes `object` rather than an index-signature type so the typed query
 * interfaces can be passed directly. A readonly interface has no index
 * signature, so requiring `Record<string, unknown>` would force every caller to
 * cast — which would defeat the point of having typed queries at all.
 */
function toHttpParams(source: object): HttpParams {
  let params = new HttpParams();

  for (const [key, value] of Object.entries(source)) {
    if (value === undefined || value === null || value === '') {
      continue;
    }

    params = params.set(key, String(value));
  }

  return params;
}
