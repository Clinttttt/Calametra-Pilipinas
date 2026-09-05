import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { type Observable } from 'rxjs';

import {
  type EarthquakeActivity,
  type EarthquakeDetail,
  type EarthquakeQuery,
  type EarthquakeSummary,
  type HazardFeatureAttributes,
  type HazardLayer,
  type MapDataResponse,
  type PaginatedList,
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

  /** Returns the hazard layer catalogue, with attribution and explainers. */
  listHazardLayers(lens?: HazardLayer['lens']): Observable<readonly HazardLayer[]> {
    return this.http.get<readonly HazardLayer[]>('/api/hazard-layers', {
      params: toHttpParams({ lens }),
    });
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
