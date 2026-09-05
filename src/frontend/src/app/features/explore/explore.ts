import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { Map as MapLibreMap, NavigationControl, ScaleControl } from 'maplibre-gl';

import { APP_CONFIG } from '../../core/config/app-config';
import { CalametraApi } from '../../core/api/calametra-api';
import { EventDetail } from './panels/event-detail';
import { Icon } from '../../shared/ui/icon/icon';
import { MapLegend } from './map/map-legend';
import { PresentationStore } from '../../core/presentation/presentation-store';
import { Timeline } from './timeline/timeline';
import {
  assignedDepthRingRadiusExpression,
  depthColourExpression,
  magnitudeRadiusExpression,
} from '../../core/visual/depth-scale';
import { firstValueFrom } from 'rxjs';
import { toEarthquakeGeoJson } from '../../core/visual/earthquake-geojson';
import {
  type EarthquakeActivity,
  type EarthquakeDetail,
  type ObservationSummary,
} from '../../core/api/contracts';
import { type IconName } from '../../shared/ui/icon/icon-paths';

/** The controlled camera perspectives offered to the user. */
type CameraMode = 'top' | 'tilt' | 'terrain';

interface CameraOption {
  readonly mode: CameraMode;
  readonly label: string;
  readonly icon: IconName;
  readonly pitch: number;
  readonly terrain: boolean;
}

/** Which tool panel is open, if any. Only one at a time. */
type OpenTool = 'timeline' | 'legend' | null;

/**
 * The main interactive map.
 *
 * ── Layout ─────────────────────────────────────────────────────────────────
 * The map holds the whole viewport and every control floats over it. Tools live on
 * a right-hand rail of low-opacity icons and open into panels on demand, rather
 * than being permanently on screen. That is the difference between an instrument
 * and a dashboard: the controls are findable when wanted and absent when not, and
 * the map is never cropped to make room for them.
 *
 * Presentation mode collapses the application chrome entirely, leaving the map and
 * a small strip of tools. It exists for projecting during a defence.
 *
 * ── Camera ─────────────────────────────────────────────────────────────────
 * Transitions are slow on purpose so the user keeps their geographic bearings
 * while the view changes. Terrain exaggeration is 1.4x — low enough to stay
 * honest, since dramatic relief would look impressive and misrepresent the
 * landscape.
 */
@Component({
  selector: 'cal-explore',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, MapLegend, EventDetail, Timeline],
  templateUrl: './explore.html',
  styleUrl: './explore.scss',
})
export class Explore {
  /** Layer and source ids, declared once so nothing hard-codes a string. */
  private static readonly hillshadeLayerId = 'calametra-hillshade';
  private static readonly earthquakeSourceId = 'calametra-earthquakes';
  private static readonly earthquakeLayerId = 'calametra-earthquakes-circles';
  private static readonly earthquakeHaloLayerId = 'calametra-earthquakes-halo';

  private readonly config = inject(APP_CONFIG);
  private readonly destroyRef = inject(DestroyRef);
  private readonly api = inject(CalametraApi);
  private readonly presentationStore = inject(PresentationStore);
  private readonly canvas = viewChild.required<ElementRef<HTMLDivElement>>('canvas');

  private map?: MapLibreMap;

  /**
   * Origin time and magnitude of every loaded event.
   *
   * Kept so the visible count can be recomputed without re-querying the map or the
   * API as the filters move.
   */
  private loadedEvents: { epochMs: number; magnitude: number | null }[] = [];

  protected readonly ready = signal(false);
  protected readonly cameraMode = signal<CameraMode>('top');
  protected readonly presenting = this.presentationStore.presenting;

  /** Which tool panel is open. */
  protected readonly openTool = signal<OpenTool>(null);

  protected readonly eventCount = signal<number | null>(null);
  protected readonly loadFailed = signal(false);
  protected readonly activity = signal<EarthquakeActivity | null>(null);

  /** How many events are visible under the current filters. */
  protected readonly visibleCount = signal<number | null>(null);

  /** True when any filter is narrowing the archive. */
  protected readonly filtered = computed(() => this.visibleCount() !== null);

  // ---- Selected event -----------------------------------------------------

  protected readonly selectedEventId = signal<string | null>(null);
  protected readonly selectedDetail = signal<EarthquakeDetail | null>(null);
  protected readonly detailLoading = signal(false);
  protected readonly detailFailed = signal(false);

  // ---- Filters ------------------------------------------------------------

  private readonly timeInstantMs = signal<number | null>(null);
  private readonly magnitudeFloor = signal<number | null>(null);

  protected readonly cameraOptions: readonly CameraOption[] = [
    { mode: 'top', label: 'Plan', icon: 'camera-top', pitch: 0, terrain: false },
    { mode: 'tilt', label: 'Oblique', icon: 'camera-tilt', pitch: 52, terrain: false },
    { mode: 'terrain', label: 'Terrain', icon: 'camera-terrain', pitch: 62, terrain: true },
  ];

  constructor() {
    // afterNextRender, because MapLibre needs a laid-out element with real
    // dimensions before it will size its WebGL canvas correctly.
    afterNextRender(() => this.initialiseMap());

    this.destroyRef.onDestroy(() => {
      // WebGL contexts are a limited browser resource and are not garbage collected
      // promptly. Navigating away without this leaks the context, and after a
      // handful of navigations the browser refuses to create more.
      this.map?.remove();
      this.map = undefined;
      this.presentationStore.exit();
    });
  }

  // ---- Tools --------------------------------------------------------------

  protected toggleTool(tool: Exclude<OpenTool, null>): void {
    this.openTool.update((open) => (open === tool ? null : tool));

    // The map's canvas size changes when a drawer opens or closes.
    requestAnimationFrame(() => this.map?.resize());
  }

  protected closeTool(): void {
    this.openTool.set(null);
    requestAnimationFrame(() => this.map?.resize());
  }

  protected togglePresentation(): void {
    this.presentationStore.toggle();
    requestAnimationFrame(() => this.map?.resize());
  }

  // ---- Map ----------------------------------------------------------------

  private initialiseMap(): void {
    const { initialView, basemapStyleUrl } = this.config;

    const map = new MapLibreMap({
      container: this.canvas().nativeElement,
      style: basemapStyleUrl,
      center: [initialView.longitude, initialView.latitude],
      zoom: initialView.zoom,
      // Generous. maxBounds constrains the viewport, not just the centre, so a box
      // drawn tightly around the archipelago silently raises the minimum usable zoom
      // on a wide screen — the map refuses to zoom out far enough to show the whole
      // country. The box exists only to stop users wandering to another continent,
      // and it does that just as well with a wide margin.
      maxBounds: [
        [104.0, -5.0],
        [141.0, 31.0],
      ],
      minZoom: 3.6,
      maxZoom: 15,
      attributionControl: { compact: true },
      pitchWithRotate: true,
    });

    map.addControl(new NavigationControl({ visualizePitch: true }), 'bottom-right');
    map.addControl(new ScaleControl({ maxWidth: 96, unit: 'metric' }), 'bottom-right');

    map.on('load', () => {
      this.applyBasemapPalette(map);
      this.addTerrainSource(map);
      this.addEarthquakeLayers(map);
      this.frameStudyArea(map, false);
      this.ready.set(true);
      void this.loadEarthquakes(map);
      void this.loadActivity();
    });

    this.map = map;
  }

  /**
   * Frames the whole archipelago.
   *
   * `fitBounds` rather than a fixed zoom, because the correct zoom depends on the
   * viewport: a value that frames the country on a laptop crops it on a narrow
   * window and leaves it small on a wide monitor. Computing it from the actual
   * container is the only way "show me the Philippines" means the same thing
   * everywhere.
   */
  private frameStudyArea(map: MapLibreMap, animate = true): void {
    map.fitBounds(
      [
        // Matches Domain.Geospatial.PhilippineStudyArea.
        [115.5, 4.0],
        [128.0, 22.0],
      ],
      {
        // Leaves room for the camera group, tool rail and readout that float over
        // the map's edges.
        padding: { top: 64, bottom: 72, left: 72, right: 88 },
        duration: animate ? 1200 : 0,
        essential: true,
      },
    );
  }

  /** Returns the camera to the national view. */
  protected resetView(): void {
    if (this.map) {
      this.frameStudyArea(this.map);
    }
  }

  /**
   * Re-paints the third-party basemap to Calametra's palette.
   *
   * The stock dark style inverts figure and ground — it paints water lighter than
   * land, so the coastline that users orient themselves by disappears — and its
   * palette is neutral grey against an interface built on a cool ramp. Overriding
   * paint at runtime is preferred over forking the style JSON: the upstream style
   * keeps improving, and this asserts only the few colours that matter.
   */
  private applyBasemapPalette(map: MapLibreMap): void {
    const land = '#0f151d';
    const water = '#070a0f';
    const vegetation = '#131a23';

    const overrides: readonly { id: string; property: string; value: string }[] = [
      { id: 'background', property: 'background-color', value: land },
      { id: 'water', property: 'fill-color', value: water },
      { id: 'landcover_wood', property: 'fill-color', value: vegetation },
      { id: 'landuse_park', property: 'fill-color', value: vegetation },
      { id: 'landuse_residential', property: 'fill-color', value: '#121821' },
      { id: 'landcover_ice_shelf', property: 'fill-color', value: land },
      { id: 'landcover_glacier', property: 'fill-color', value: land },
    ];

    for (const override of overrides) {
      // Guarded: the upstream style may rename or drop a layer, and a missing layer
      // must not take the whole map down.
      if (map.getLayer(override.id)) {
        map.setPaintProperty(override.id, override.property, override.value);
      }
    }
  }

  /**
   * Registers the DEM source and the hillshade layer that makes it visible.
   *
   * Terrain needs both. `setTerrain` deforms the mesh so geometry follows elevation,
   * which produces a correct perspective — but a flat-coloured surface reflects no
   * light, so the relief is invisible even though it is present.
   */
  private addTerrainSource(map: MapLibreMap): void {
    if (!map.getSource('terrain-dem')) {
      map.addSource('terrain-dem', {
        type: 'raster-dem',
        tiles: [this.config.terrainTileUrl],
        encoding: 'terrarium',
        tileSize: 256,
        maxzoom: 13,
        attribution:
          'Elevation: <a href="https://registry.opendata.aws/terrain-tiles/">AWS Terrain Tiles</a>',
      });
    }

    if (map.getLayer(Explore.hillshadeLayerId)) {
      return;
    }

    // Beneath the first symbol layer so place labels stay legible on top of the
    // shading rather than being buried by it.
    const firstSymbolLayer = map.getStyle().layers?.find((layer) => layer.type === 'symbol')?.id;

    map.addLayer(
      {
        id: Explore.hillshadeLayerId,
        type: 'hillshade',
        source: 'terrain-dem',
        layout: { visibility: 'none' },
        paint: {
          'hillshade-shadow-color': '#02040a',
          'hillshade-highlight-color': '#42596d',
          'hillshade-accent-color': '#0b1219',
          'hillshade-exaggeration': 0.6,
          // Lit from the north-west, the cartographic convention. Lighting relief
          // from below inverts how the eye reads ridges and valleys.
          'hillshade-illumination-direction': 315,
        },
      },
      firstSymbolLayer,
    );
  }

  /**
   * Creates the earthquake source and its two circle layers.
   *
   * Two layers because a single circle cannot carry both a depth fill and a distinct
   * outline for unmeasured depths. The ring sits beneath and is visible only for
   * events whose depth was assigned by the agency.
   */
  private addEarthquakeLayers(map: MapLibreMap): void {
    map.addSource(Explore.earthquakeSourceId, {
      type: 'geojson',
      data: { type: 'FeatureCollection', features: [] },
    });

    // 43% of the archive carries an assigned depth, so this has to stay quiet — a
    // thin ring, not a warning fill. Hidden below zoom 8, where the rings would
    // merge into a haze and read as noise.
    map.addLayer({
      id: Explore.earthquakeHaloLayerId,
      type: 'circle',
      source: Explore.earthquakeSourceId,
      minzoom: 8,
      filter: ['!', ['coalesce', ['get', 'depthMeasured'], true]],
      paint: {
        'circle-radius': assignedDepthRingRadiusExpression() as never,
        'circle-color': 'transparent',
        'circle-stroke-width': 1,
        'circle-stroke-color': '#d9a53b',
        'circle-stroke-opacity': 0.5,
      },
    });

    map.addLayer({
      id: Explore.earthquakeLayerId,
      type: 'circle',
      source: Explore.earthquakeSourceId,
      paint: {
        'circle-radius': magnitudeRadiusExpression() as never,
        'circle-color': depthColourExpression() as never,
        // Density is carried by opacity rather than by shrinking markers into
        // invisibility. Overlapping events read as a darker cluster, which is the
        // information, while a single event stays clearly visible.
        'circle-opacity': ['interpolate', ['linear'], ['zoom'], 4.5, 0.6, 7, 0.68, 10, 0.78],
        'circle-stroke-width': ['interpolate', ['linear'], ['zoom'], 7, 0, 9, 0.75],
        'circle-stroke-color': '#05080c',
        'circle-stroke-opacity': 0.9,
      },
    });

    this.wireEarthquakeInteraction(map);
  }

  /**
   * Loads the archive in one compact request.
   *
   * Measured at 2.3x smaller on the wire than paging the search endpoint, in one
   * round trip instead of 28. The map needs position, magnitude, depth quality and
   * origin time; it never reads the agency names or formatted strings that make the
   * search payload four times heavier.
   */
  private async loadEarthquakes(map: MapLibreMap): Promise<void> {
    try {
      const archive = await firstValueFrom(this.api.getEarthquakeMapData());

      const source = map.getSource(Explore.earthquakeSourceId);

      if (source && 'setData' in source) {
        (source as { setData: (data: unknown) => void }).setData(
          toEarthquakeGeoJson(archive.points),
        );
      }

      this.loadedEvents = archive.points.map((point) => ({
        epochMs: point.t,
        magnitude: point.m,
      }));

      this.eventCount.set(archive.count);
    } catch {
      // The HTTP interceptor already surfaced a toast; this drives the on-map state
      // so the user is not left wondering whether an empty map means "no events" or
      // "nothing loaded".
      this.loadFailed.set(true);
    }
  }

  private async loadActivity(): Promise<void> {
    try {
      this.activity.set(await firstValueFrom(this.api.getEarthquakeActivity()));
    } catch {
      this.activity.set(null);
    }
  }

  // ---- Filters ------------------------------------------------------------

  /**
   * Filters visible markers by instant and magnitude floor.
   *
   * Applied as MapLibre filter expressions rather than by refetching or rebuilding
   * the GeoJSON source. Playback moves the instant several times a second, and
   * re-serialising 27,000 features on each step would stutter; a filter on numeric
   * properties is evaluated on the GPU.
   */
  private applyFilters(): void {
    const map = this.map;

    if (!map) {
      return;
    }

    const instantMs = this.timeInstantMs();
    const magnitudeFloor = this.magnitudeFloor();
    const clauses: unknown[] = [];

    if (instantMs !== null) {
      clauses.push(['<=', ['get', 'epochMs'], instantMs]);
    }

    if (magnitudeFloor !== null) {
      // Events with no reported magnitude are excluded rather than treated as zero:
      // an unmeasured magnitude is not evidence of a small earthquake.
      clauses.push(['>=', ['coalesce', ['get', 'magnitude'], -1], magnitudeFloor]);
    }

    const baseFilter = clauses.length === 0 ? null : ['all', ...clauses];
    const unmeasuredDepth = ['!', ['coalesce', ['get', 'depthMeasured'], true]];

    for (const layerId of [Explore.earthquakeLayerId, Explore.earthquakeHaloLayerId]) {
      if (!map.getLayer(layerId)) {
        continue;
      }

      // The ring layer carries its own predicate, so its filter is the conjunction
      // rather than a replacement.
      const layerFilter =
        layerId === Explore.earthquakeHaloLayerId
          ? clauses.length === 0
            ? unmeasuredDepth
            : ['all', unmeasuredDepth, ...clauses]
          : baseFilter;

      map.setFilter(layerId, layerFilter as never);
    }

    this.recountVisible();
  }

  private recountVisible(): void {
    const instantMs = this.timeInstantMs();
    const magnitudeFloor = this.magnitudeFloor();

    if (instantMs === null && magnitudeFloor === null) {
      this.visibleCount.set(null);

      return;
    }

    // Counted from the loaded event list rather than from rendered features:
    // queryRenderedFeatures only sees the current viewport, which would make the
    // count change as the user pans.
    this.visibleCount.set(
      this.loadedEvents.filter(
        (event) =>
          (instantMs === null || event.epochMs <= instantMs) &&
          (magnitudeFloor === null || (event.magnitude ?? -1) >= magnitudeFloor),
      ).length,
    );
  }

  protected onInstantChanged(instantMs: number | null): void {
    this.timeInstantMs.set(instantMs);
    this.applyFilters();
  }

  protected onMagnitudeFloorChanged(floor: number | null): void {
    this.magnitudeFloor.set(floor);
    this.applyFilters();
  }

  // ---- Selection ----------------------------------------------------------

  private wireEarthquakeInteraction(map: MapLibreMap): void {
    map.on('mouseenter', Explore.earthquakeLayerId, () => {
      map.getCanvas().style.cursor = 'pointer';
    });

    map.on('mouseleave', Explore.earthquakeLayerId, () => {
      map.getCanvas().style.cursor = '';
    });

    map.on('click', Explore.earthquakeLayerId, (event) => {
      const eventId = event.features?.[0]?.properties?.['id'];

      if (typeof eventId === 'string') {
        void this.selectEvent(eventId);
      }
    });

    // Clicking empty map dismisses the panel: on a map, clicking away is the
    // natural gesture for "I'm done with that".
    map.on('click', (event) => {
      const hits = map.queryRenderedFeatures(event.point, {
        layers: [Explore.earthquakeLayerId],
      });

      if (hits.length === 0) {
        this.clearSelection();
      }
    });
  }

  /**
   * Loads the full detail for an event and opens the panel.
   *
   * A separate request rather than reusing the marker's properties: the list carries
   * one reading, and showing a partial comparison would undercut the point of the
   * panel.
   */
  private async selectEvent(eventId: string): Promise<void> {
    this.selectedEventId.set(eventId);
    this.detailFailed.set(false);
    this.detailLoading.set(true);
    this.selectedDetail.set(null);

    try {
      const detail = await firstValueFrom(this.api.getEarthquakeDetail(eventId));

      // Guard against a slow response for an event the user has moved on from:
      // without this, clicking two markers quickly can show the wrong one.
      if (this.selectedEventId() === eventId) {
        this.selectedDetail.set(detail);
      }
    } catch {
      if (this.selectedEventId() === eventId) {
        this.detailFailed.set(true);
      }
    } finally {
      if (this.selectedEventId() === eventId) {
        this.detailLoading.set(false);
      }
    }
  }

  protected clearSelection(): void {
    this.selectedEventId.set(null);
    this.selectedDetail.set(null);
    this.detailLoading.set(false);
    this.detailFailed.set(false);
  }

  protected locateObservation(observation: ObservationSummary): void {
    this.map?.flyTo({
      center: [observation.longitude, observation.latitude],
      zoom: Math.max(this.map.getZoom(), 9),
      duration: 1400,
      essential: true,
    });
  }

  // ---- Camera -------------------------------------------------------------

  protected setCamera(option: CameraOption): void {
    const map = this.map;

    if (!map) {
      return;
    }

    this.cameraMode.set(option.mode);

    map.setTerrain(option.terrain ? { source: 'terrain-dem', exaggeration: 1.4 } : null);

    if (map.getLayer(Explore.hillshadeLayerId)) {
      map.setLayoutProperty(
        Explore.hillshadeLayerId,
        'visibility',
        option.terrain ? 'visible' : 'none',
      );
    }

    map.easeTo({
      pitch: option.pitch,
      duration: 900,
      // Matches --ease-out, so camera motion feels like the rest of the interface.
      easing: (t) => 1 - Math.pow(1 - t, 3),
    });
  }
}
