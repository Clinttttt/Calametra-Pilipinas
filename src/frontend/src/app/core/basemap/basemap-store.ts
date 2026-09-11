import { Injectable, signal } from '@angular/core';

/**
 * A base layer the reader can choose.
 *
 * Each exists for an analytical reason rather than as decoration, which is the test for whether
 * it belongs here. A basemap that only looked different would be noise.
 */
export interface BasemapOption {
  readonly id: BasemapId;
  readonly label: string;
  /** Why a reader would pick this one. Shown in the control, not hidden in a tooltip. */
  readonly purpose: string;
  /** Raster tile template, or null for the vector base recoloured in place. */
  readonly rasterTileUrl: string | null;
  /** Highest zoom the source provides. Beyond it the raster is overzoomed. */
  readonly maxZoom: number;
  /** Required credit, shown while the layer is active. */
  readonly attribution: string;
  /**
   * A licence note where the terms need confirming before publication. Null when the source
   * is unambiguous.
   */
  readonly licenceCaveat: string | null;
}

export type BasemapId = 'dark' | 'grayscale' | 'bathymetry' | 'satellite';

const STORAGE_KEY = 'calametra.basemap';

/**
 * BASE LAYERS
 *
 * ── Why base layers rather than a light interface theme ──────────────────────
 * A light UI theme was tried and removed. It fought the data: the depth ramp runs warm red to
 * cool blue and is calibrated against a dark ground, and on pale land the unmeasured-depth grey
 * turned muddy while the whole view lost the instrument quality the design depends on. Base
 * layers change what the map is *of* rather than what the interface is made of, which is the
 * choice that actually serves analysis.
 *
 * ── Why raster overlays rather than swapping the style ───────────────────────
 * `map.setStyle()` discards every source and layer the application has added — the earthquake
 * field, fault geometry, cyclone tracks, cross-section line — and each would have to be rebuilt
 * and its data refetched. So the vector style is loaded once and never replaced: imagery is
 * added as a raster layer beneath the data, and the two vector options are the same style
 * recoloured in place.
 *
 * ── Why only four ───────────────────────────────────────────────────────────
 * Every source here was probed before being offered. Sources whose terms could not be
 * established are not included: this platform already refuses to store PHIVOLCS vector data
 * without a signed agreement, and it would be inconsistent to ship a basemap on looser
 * reasoning than that.
 */
export const BASEMAPS: readonly BasemapOption[] = [
  {
    id: 'dark',
    label: 'Dark',
    purpose: 'Default. Highest contrast for the depth colour ramp.',
    rasterTileUrl: null,
    maxZoom: 20,
    attribution: '© OpenStreetMap contributors, via OpenFreeMap',
    licenceCaveat: null,
  },
  {
    id: 'grayscale',
    label: 'Grayscale',
    purpose: 'Neutral ground for figures and print, where hue must belong to the data alone.',
    rasterTileUrl: null,
    maxZoom: 20,
    attribution: '© OpenStreetMap contributors, via OpenFreeMap',
    licenceCaveat: null,
  },
  {
    id: 'bathymetry',
    label: 'Bathymetry',
    purpose:
      'Sea-floor relief. Shows the trench systems the deep seismicity belongs to — the '
      + 'Philippine, Manila, Negros and Cotabato trenches are visible as bathymetric features.',
    rasterTileUrl:
      'https://gibs.earthdata.nasa.gov/wmts/epsg3857/best/BlueMarble_ShadedRelief_Bathymetry/'
      + 'default/GoogleMapsCompatible_Level8/{z}/{y}/{x}.jpeg',
    // Level8 is the deepest the layer publishes. Overzooming past it blurs rather than fails,
    // which is acceptable for a regional basemap on a nationally scoped platform.
    maxZoom: 8,
    attribution: 'NASA Earth Observations — Blue Marble shaded relief and bathymetry',
    licenceCaveat: null,
  },
  {
    id: 'satellite',
    label: 'Satellite',
    purpose: 'Land cover and settlement context around an epicentre.',
    rasterTileUrl:
      'https://services.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/'
      + 'tile/{z}/{y}/{x}',
    maxZoom: 18,
    attribution: 'Imagery © Esri, Maxar, Earthstar Geographics',
    licenceCaveat:
      'Esri imagery is served for use with attribution. Confirm the terms apply to published '
      + 'figures before reproducing this view in the thesis.',
  },
];

/**
 * Owns which base layer is shown.
 *
 * Persisted, because a reader who chose grayscale to take figures should not have to choose it
 * again on every visit.
 */
@Injectable({ providedIn: 'root' })
export class BasemapStore {
  private readonly _selectedId = signal<BasemapId>(readStored());

  readonly options = BASEMAPS;
  readonly selectedId = this._selectedId.asReadonly();

  /** The full option, so callers do not look it up themselves. */
  selected(): BasemapOption {
    return BASEMAPS.find((option) => option.id === this._selectedId()) ?? BASEMAPS[0];
  }

  select(id: BasemapId): void {
    if (id === this._selectedId()) {
      return;
    }

    this._selectedId.set(id);

    try {
      localStorage.setItem(STORAGE_KEY, id);
    } catch {
      // Unavailable in private modes. The choice still applies for this session; only the
      // memory of it is lost, which is not worth failing on.
    }
  }
}

function readStored(): BasemapId {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);

    if (BASEMAPS.some((option) => option.id === stored)) {
      return stored as BasemapId;
    }
  } catch {
    // Falls through to the default.
  }

  return 'dark';
}
