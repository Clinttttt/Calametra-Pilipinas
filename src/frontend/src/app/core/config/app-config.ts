import { InjectionToken } from '@angular/core';

/**
 * Runtime configuration.
 *
 * Provided through an injection token rather than imported from an
 * `environment.ts` file, so that a single built artefact can be pointed at a
 * different API without a rebuild, and so tests can supply their own values
 * without touching global state.
 */
export interface AppConfig {
  /** Base URL of the Calametra API, without a trailing slash. */
  readonly apiBaseUrl: string;

  /** Vector basemap style URL. */
  readonly basemapStyleUrl: string;

  /**
   * Terrain DEM tile template.
   *
   * AWS Terrain Tiles, Terrarium encoding — public, no credentials, global
   * coverage. Verified serving Philippine tiles at zoom 10 to 12 on 2026-09-04.
   */
  readonly terrainTileUrl: string;

  /** Opening camera position: the Philippine archipelago. */
  readonly initialView: {
    readonly longitude: number;
    readonly latitude: number;
    readonly zoom: number;
  };
}

export const APP_CONFIG = new InjectionToken<AppConfig>('APP_CONFIG');

/**
 * Development defaults.
 *
 * The basemap is OpenFreeMap, an OpenStreetMap-derived vector tile service that
 * requires no API key or account. Chosen over Protomaps' hosted service, which
 * returns HTTP 403 without a key — verified 2026-09-04 — because a basemap that
 * needs a signup before the map will render is a poor default for anyone cloning
 * the repository.
 *
 * For deployment, a Philippines-only extract can be built into a single `.pmtiles`
 * file and self-hosted. That removes the third-party runtime dependency entirely
 * and keeps the region's basemap available offline, which matters for a platform
 * people may consult during poor connectivity.
 */
export const DEFAULT_APP_CONFIG: AppConfig = {
  // Matches the `http` launch profile in Calametra.Api/Properties/launchSettings.json,
  // which is the profile `dotnet run` selects by default.
  apiBaseUrl: 'http://localhost:5130',
  basemapStyleUrl: 'https://tiles.openfreemap.org/styles/dark',
  terrainTileUrl: 'https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png',
  initialView: {
    // Centre of the archipelago, matching Domain.Geospatial.PhilippineStudyArea.
    longitude: 122.5,
    latitude: 12.5,
    // Fits Batanes to Tawi-Tawi on a typical desktop viewport.
    zoom: 5.1,
  },
};
