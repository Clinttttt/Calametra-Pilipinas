import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { Map as MapLibreMap, NavigationControl, ScaleControl, type GeoJSONSource } from 'maplibre-gl';

import { APP_CONFIG } from '../../core/config/app-config';
import { CalametraApi } from '../../core/api/calametra-api';
import { ComparisonStore } from '../../core/comparison/comparison-store';
import { CrossSectionPlot } from './cross-section/cross-section-plot';
import { CrossSectionStore } from '../../core/cross-section/cross-section-store';
import { CyclonePanel } from './panels/cyclone-panel';
import { CycloneStore } from '../../core/cyclones/cyclone-store';
import { EventComparison } from './panels/event-comparison';
import { EventDetail } from './panels/event-detail';
import { HazardLayerStore, type LayerState } from '../../core/layers/hazard-layer-store';
import { hazardRasterSource } from '../../core/layers/hazard-tile-source';
import { Icon } from '../../shared/ui/icon/icon';
import { LayersPanel } from './panels/layers-panel';
import { MapLegend } from './map/map-legend';
import { PresentationStore } from '../../core/presentation/presentation-store';
import { PlacePanel } from './panels/place-panel';
import { PlaceStore } from '../../core/places/place-store';
import { radiusRing } from '../../core/places/radius-ring';
import { SimilarEvents } from './panels/similar-events';
import { SimilarEventsStore } from '../../core/similar-events/similar-events-store';
import { BasemapStore } from '../../core/basemap/basemap-store';
import { applyBasemap } from '../../core/basemap/apply-basemap';
import { HazardModeStore, type HazardMode } from '../../core/hazards/hazard-mode-store';
import { HazardChooser } from './hazard-chooser/hazard-chooser';
import { Timeline } from './timeline/timeline';
import {
  assignedDepthRingRadiusExpression,
  depthColourExpression,
  magnitudeRadiusExpression,
} from '../../core/visual/depth-scale';
import {
  FAULT_CASING_COLOUR,
  describeSlipType,
  faultCasingWidthExpression,
  faultColourExpression,
  faultLineWidthExpression,
  faultWidthMultiplierExpression,
} from '../../core/visual/fault-style';
import { firstValueFrom } from 'rxjs';
import { toEarthquakeGeoJson } from '../../core/visual/earthquake-geojson';
import { toCycloneGeoJson } from '../../core/visual/cyclone-track';
import { toWindFieldGeoJson } from '../../core/visual/cyclone-field';
import { createCycloneSymbol, cycloneSymbolVariants } from '../../core/visual/cyclone-symbol';
import {
  type EarthquakeActivity,
  type EarthquakeDetail,
  type HazardFeatureAttributes,
  type HazardLayer,
  type ObservationSummary,
  type CycloneFix,
  type CycloneTrack,
  type PlaceEvent,
  type PlaceMatch,
  type SimilarEarthquake,
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

/**
 * One piece of cross-section geometry: the line itself, or one of its endpoints.
 *
 * Declared structurally rather than pulled from a `GeoJSON` namespace, matching
 * `EarthquakeGeoJson` — the project types its map payloads by shape so the compiler
 * checks the properties MapLibre expressions actually read.
 */
interface SectionFeature {
  readonly type: 'Feature';
  readonly properties: { readonly role: 'line' | 'endpoint'; readonly label?: string };
  readonly geometry:
    | { readonly type: 'LineString'; readonly coordinates: readonly [number, number][] }
    | { readonly type: 'Point'; readonly coordinates: readonly [number, number] };
}

/** Which tool panel is open, if any. Only one at a time. */
type OpenTool = 'hazards' | 'timeline' | 'legend' | 'layers' | null;

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
  imports: [
    Icon,
    MapLegend,
    CyclonePanel,
    EventComparison,
    EventDetail,
    LayersPanel,
    Timeline,
    CrossSectionPlot,
    SimilarEvents,
    HazardChooser,
    PlacePanel,
  ],
  templateUrl: './explore.html',
  styleUrl: './explore.scss',
})
export class Explore {
  /** Layer and source ids, declared once so nothing hard-codes a string. */
  private static readonly hillshadeLayerId = 'calametra-hillshade';
  private static readonly earthquakeSourceId = 'calametra-earthquakes';
  private static readonly earthquakeLayerId = 'calametra-earthquakes-circles';
  private static readonly earthquakeHaloLayerId = 'calametra-earthquakes-halo';
  private static readonly sectionSourceId = 'calametra-section';
  private static readonly sectionLineLayerId = 'calametra-section-line';
  private static readonly sectionEndpointLayerId = 'calametra-section-endpoints';
  private static readonly comparisonSourceId = 'calametra-comparison';
  private static readonly comparisonLineLayerId = 'calametra-comparison-line';
  private static readonly comparisonMarkerLayerId = 'calametra-comparison-markers';
  private static readonly comparisonLabelLayerId = 'calametra-comparison-labels';
  private static readonly cycloneSourceId = 'calametra-cyclone-tracks';
  private static readonly cycloneCasingLayerId = 'calametra-cyclone-casing';
  private static readonly cycloneFaintLayerId = 'calametra-cyclone-faint';
  private static readonly cycloneTrackLayerId = 'calametra-cyclone-track';
  private static readonly cycloneFixLayerId = 'calametra-cyclone-fix';
  private static readonly cycloneLandfallLayerId = 'calametra-cyclone-landfall';
  private static readonly cyclonePositionLayerId = 'calametra-cyclone-position-marker';
  private static readonly cycloneFieldSourceId = 'calametra-cyclone-field';
  private static readonly cycloneGaleFillLayerId = 'calametra-cyclone-gale-fill';
  private static readonly cycloneGaleEdgeLayerId = 'calametra-cyclone-gale-edge';
  private static readonly cycloneEyewallLayerId = 'calametra-cyclone-eyewall';
  private static readonly imagerySourceId = 'calametra-imagery';
  private static readonly imageryLayerId = 'calametra-imagery-raster';
  private static readonly placeSourceId = 'calametra-place';
  private static readonly placeRingFillLayerId = 'calametra-place-ring-fill';
  private static readonly placeRingEdgeLayerId = 'calametra-place-ring-edge';
  private static readonly placeCentreLayerId = 'calametra-place-centre';

  /**
   * Every layer belonging to one hazard, so visibility can be switched as a group.
   *
   * Listed explicitly rather than derived from a prefix: a missing entry here is a layer that keeps
   * drawing under the wrong hazard, and a string prefix would silently pick up any future layer that
   * happened to share it. Earthquake geometry was already switched this way; cyclone geometry was
   * not, and relied on a chain of effects emptying its two sources instead — so a storm track could
   * outlive the switch to earthquakes if any link in that chain did not fire.
   */
  private static readonly earthquakeLayerIds = [
    Explore.earthquakeLayerId,
    Explore.earthquakeHaloLayerId,
    // The place ring belongs to this group because the context it illustrates is seismic: the
    // panel counts earthquakes and names fault traces. Listed here rather than left to the
    // store's own clearing, which is the mistake the cyclone geometry made.
    Explore.placeRingFillLayerId,
    Explore.placeRingEdgeLayerId,
    Explore.placeCentreLayerId,
  ] as const;

  private static readonly cycloneLayerIds = [
    Explore.cycloneCasingLayerId,
    Explore.cycloneFaintLayerId,
    Explore.cycloneTrackLayerId,
    Explore.cycloneFixLayerId,
    Explore.cycloneLandfallLayerId,
    Explore.cycloneGaleFillLayerId,
    Explore.cycloneGaleEdgeLayerId,
    Explore.cycloneEyewallLayerId,
    Explore.cyclonePositionLayerId,
  ] as const;

  /**
   * The magnitude the map opens on. Shared with the Time Machine's "M6.0+ only" control so the two
   * cannot describe different thresholds.
   */
  private static readonly openingMagnitudeFloor = 6;

  private readonly config = inject(APP_CONFIG);
  private readonly destroyRef = inject(DestroyRef);
  private readonly api = inject(CalametraApi);
  private readonly layerStore = inject(HazardLayerStore);
  private readonly presentationStore = inject(PresentationStore);
  private readonly crossSectionStore = inject(CrossSectionStore);
  private readonly similarEventsStore = inject(SimilarEventsStore);
  private readonly comparisonStore = inject(ComparisonStore);
  private readonly cycloneStore = inject(CycloneStore);
  private readonly placeStore = inject(PlaceStore);
  private readonly basemapStore = inject(BasemapStore);
  private readonly hazardStore = inject(HazardModeStore);
  private readonly canvas = viewChild.required<ElementRef<HTMLDivElement>>('canvas');

  private map?: MapLibreMap;

  /**
   * Layer ids currently present in the MapLibre style, keyed by catalogue id.
   *
   * Tracked because MapLibre has no "set the visible layers to this list" operation —
   * reflecting declarative state onto it means diffing against what is already there.
   */
  private readonly attachedLayers = new Map<string, readonly string[]>();

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

  // ---- Which hazard is being explored -------------------------------------

  protected readonly choosingHazard = this.hazardStore.choosing;
  protected readonly activeHazard = this.hazardStore.active;
  protected readonly earthquakesActive = this.hazardStore.isEarthquakes;
  protected readonly cyclonesActive = this.hazardStore.isCyclones;

  /** The layer lens belonging to the active hazard, or null before one is chosen. */
  protected readonly activeLens = computed(() => this.hazardStore.active()?.lens ?? null);

  /**
   * Whether the active hazard has any published overlays to configure.
   *
   * Drives whether the layers control appears at all. Only Seismic layers are catalogued today, so
   * offering the control under cyclones would open a panel with nothing in it.
   */
  protected readonly hasHazardLayers = computed(
    () => this.layerStore.forLens(this.activeLens()).length > 0,
  );

  /** Guards the one-off archive fetch, so returning to earthquakes does not refetch it. */
  private earthquakesRequested = false;

  /** Which tool panel is open. */
  protected readonly openTool = signal<OpenTool>(null);

  /** True while the cross-section tool is open in any phase. */
  protected readonly sectionActive = computed(() => this.crossSectionStore.phase() !== 'idle');

  /** Which event the similarity panel is showing, if any. */
  protected readonly similarEventId = this.similarEventsStore.eventId;

  /** True while a pair of events is being compared. */
  protected readonly comparisonActive = this.comparisonStore.active;

  /** True while the cyclone panel is open. */
  protected readonly cyclonesOpen = this.cycloneStore.open;

  /** True while the place explorer is open. */
  protected readonly placesOpen = this.placeStore.open;

  /** Catalogue of hazard layers and which are switched on. */
  protected readonly hazardLayers = this.layerStore.layers;

  /**
   * Attributes of an inspected hazard feature, if any.
   *
   * Kept separate from the earthquake selection: a fault and an earthquake are
   * different kinds of thing, and conflating them into one "selected" slot would make
   * the panel guess which it is showing.
   */
  protected readonly inspectedFeature = signal<HazardFeatureAttributes | null>(null);

  /**
   * Inspected attributes as ordered entries.
   *
   * Derived here rather than with a `keyvalue` pipe in the template: that pipe sorts
   * alphabetically by default, which would reorder a publisher's fields into an order
   * they did not choose.
   */
  protected readonly inspectedAttributes = computed(() =>
    Object.entries(this.inspectedFeature()?.attributes ?? {}).map(([key, value]) => ({
      key,
      value,
    })),
  );

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

  /**
   * The magnitude floor applied to the map.
   *
   * <b>Opens at M6.0, not at the whole catalogue.</b> Rendering all 27,241 events at once produced a
   * saturated orange mass covering the archipelago: at national zoom the symbols overlap several
   * deep, so the picture conveyed neither where earthquakes occur nor how deep they are, and the
   * one thing it did convey — density — is an artefact of instrumentation rather than of seismicity.
   *
   * 6.0 is not a display convenience, it is the threshold this archive supports. Events per decade
   * rise from 21 in the 1900s to 5,974 in the 2020s, a 285-fold increase that is entirely a change
   * in the recording network, while the M6.0+ rate is flat at roughly five per year across the same
   * 125 years. So M6.0+ is the only subset that can be compared between eras, and it is therefore
   * the only honest thing to open a historical exploration platform on. Measured against the loaded
   * archive: 631 events at M6.0+, against 5,053 at M5.0+ and 15,971 at M4.5+.
   *
   * The full catalogue is one click away and the readout states the floor in place, so nothing is
   * hidden — the default states a premise instead of dumping a population.
   */
  private readonly magnitudeFloor = signal<number | null>(Explore.openingMagnitudeFloor);

  /** Read-only views for the template and the Time Machine, so only this component mutates them. */
  protected readonly activeMagnitudeFloor = this.magnitudeFloor.asReadonly();
  protected readonly activeInstantMs = this.timeInstantMs.asReadonly();

  /**
   * The qualifier under the archive count.
   *
   * Follows the active floor rather than being hard-coded. It previously read "M4.0+" always — a
   * true statement about what the USGS catalogue contains here, but one that would now contradict
   * the map whenever a floor is applied. With no floor it still reports the catalogue's own
   * effective limit; with one, it reports the floor.
   */
  protected readonly floorLabel = computed(() => {
    const floor = this.magnitudeFloor();

    return floor === null ? 'M4.0+ · 1901–2026' : `M${floor.toFixed(1)}+ · 1901–2026`;
  });

  protected readonly cameraOptions: readonly CameraOption[] = [
    { mode: 'top', label: 'Plan', icon: 'camera-top', pitch: 0, terrain: false },
    { mode: 'tilt', label: 'Oblique', icon: 'camera-tilt', pitch: 52, terrain: false },
    { mode: 'terrain', label: 'Terrain', icon: 'camera-terrain', pitch: 62, terrain: true },
  ];

  constructor() {
    // afterNextRender, because MapLibre needs a laid-out element with real
    // dimensions before it will size its WebGL canvas correctly.
    afterNextRender(() => this.initialiseMap());

    // Reflects layer state onto the map. An effect rather than a callback on the toggle
    // so the two stay decoupled: the store owns intent and this owns the consequence,
    // and any other route to changing visibility works without extra wiring.
    effect(() => {
      const layers = this.layerStore.layers();

      if (this.map) {
        void this.syncHazardLayers(layers);
      }
    });

    // The drawn section line, kept in step with the store for the same reason.
    effect(() => {
      // Read both so the effect re-runs when either endpoint moves.
      this.crossSectionStore.start();
      this.crossSectionStore.end();

      this.syncSectionGeometry();
    });

    // The place ring. Reads the panel state as well as the selection, because closing the panel
    // has to remove the ring — an analytical circle with nothing explaining it is worse than no
    // circle at all.
    effect(() => {
      this.placeStore.selected();
      this.placeStore.radiusKm();
      this.placeStore.open();

      this.syncPlaceGeometry();
    });

    // A preset can place a section anywhere in the country, so the camera has to follow
    // it. Only for presets: when the user draws their own line they are already looking
    // at it, and moving the map under them would be disorienting.
    effect(() => {
      const preset = this.crossSectionStore.activePreset();

      if (preset && this.map) {
        this.frameSection(preset.start, preset.end);
      }
    });

    // The comparison pair, drawn as two lettered markers joined by a line. The line is the
    // point: it makes the separation the panel reports in kilometres visible as geography,
    // which two synchronised maps could not do without the reader aligning them mentally.
    effect(() => {
      const endpoints = this.comparisonStore.endpoints();

      this.syncComparisonGeometry(endpoints);
    });

    // Cyclone tracks. Rebuilt when the storm changes or a different agency is emphasised,
    // since emphasis is baked into the feature properties the paint expressions filter on.
    effect(() => {
      const track = this.cycloneStore.track();
      const emphasisedSlug = this.cycloneStore.emphasised()?.sourceSlug ?? null;
      const map = this.map;

      if (!map || !map.isStyleLoaded()) {
        return;
      }

      const source = map.getSource(Explore.cycloneSourceId) as GeoJSONSource | undefined;

      source?.setData(
        (track === null
          ? { type: 'FeatureCollection', features: [] }
          : toCycloneGeoJson(track.tracks, emphasisedSlug)) as never,
      );
    });

    // Frames a newly opened storm once, keyed on its id rather than on the track object, so
    // emphasising a different agency does not move the camera out from under the reader.
    effect(() => {
      const track = this.cycloneStore.track();

      if (track === null || track.id === this.framedCycloneId) {
        return;
      }

      this.framedCycloneId = track.id;
      this.frameCyclone(track);
    });

    // The base layer. An effect rather than a call from the switcher, so the map stays
    // decoupled from the control: any route to changing the selection recolours it.
    effect(() => {
      // Read so the effect re-runs on change.
      this.basemapStore.selectedId();

      const map = this.map;

      if (map && this.ready()) {
        this.applyBasemapPalette(map);
      }
    });

    // The measured extent at the current fix: gale field and eyewall, both to scale.
    effect(() => {
      const fix = this.cycloneStore.currentFix();
      const map = this.map;

      if (!map || !map.isStyleLoaded()) {
        return;
      }

      const source = map.getSource(Explore.cycloneFieldSourceId) as GeoJSONSource | undefined;

      source?.setData(toWindFieldGeoJson(fix) as never);
    });

    // The rotation mark. Driven by its own animation frame loop rather than by the playback
    // clock, so the storm turns continuously while the reader is paused on a single fix — a
    // stationary spiral would read as a static diagram.
    effect(() => {
      const fix = this.cycloneStore.currentFix();

      if (fix === null) {
        this.stopRotation();

        return;
      }

      this.startRotation();
    });

    // Crosshair while placing endpoints. The cursor is the only signal that the map is
    // in a different mode, so without it the click does something unexplained.
    effect(() => {
      const capturing = this.crossSectionStore.capturingClicks();
      const canvas = this.map?.getCanvas();

      if (canvas) {
        canvas.style.cursor = capturing ? 'crosshair' : '';
      }
    });

    this.destroyRef.onDestroy(() => {
      // WebGL contexts are a limited browser resource and are not garbage collected
      // promptly. Navigating away without this leaks the context, and after a
      // handful of navigations the browser refuses to create more.
      this.stopRotation();
      this.map?.remove();
      this.map = undefined;
      this.presentationStore.exit();
    });
  }

  // ---- Tools --------------------------------------------------------------

  protected toggleTool(tool: Exclude<OpenTool, null>): void {
    this.openTool.update((open) => (open === tool ? null : tool));

    // The place explorer occupies the same slot, and unlike the others it owns geometry on the
    // map. Closed here rather than left to overlap, which would put two panels at the same
    // coordinates and leave a ring drawn beneath whichever won.
    if (this.openTool() !== null) {
      this.placeStore.closePanel();
    }

    // The map's canvas size changes when a drawer opens or closes.
    requestAnimationFrame(() => this.map?.resize());
  }

  protected closeTool(): void {
    this.openTool.set(null);
    requestAnimationFrame(() => this.map?.resize());
  }

  /**
   * Opens or closes the depth cross-section.
   *
   * Closing also clears the drawn line: leaving a section line on the map with no plot
   * beneath it would be a mark with nothing to explain it.
   */
  protected toggleCrossSection(): void {
    if (this.sectionActive()) {
      this.crossSectionStore.close();
    } else {
      // The section occupies the foot of the map, where the timeline also sits.
      this.openTool.set(null);
      this.crossSectionStore.open();
    }

    requestAnimationFrame(() => this.map?.resize());
  }

  /** Opens or closes the cyclone panel. */
  protected toggleCyclones(): void {
    if (this.cyclonesOpen()) {
      this.cycloneStore.closePanel();
    } else {
      this.cycloneStore.openPanel();
    }

    requestAnimationFrame(() => this.map?.resize());
  }

  /**
   * Opens or closes the place explorer.
   *
   * Closing clears the chosen place, which is what removes the radius ring. The two are one
   * gesture on purpose: the ring is an analytical circle the reader asked for, and leaving it
   * drawn with no panel stating what it counts would make it look like mapped geography.
   */
  protected togglePlaces(): void {
    if (this.placesOpen()) {
      this.placeStore.closePanel();
    } else {
      // Shares the right-hand slot with the rail's other panels, so whichever of those is open
      // gives way rather than stacking behind it.
      this.openTool.set(null);
      this.placeStore.openPanel();
    }

    requestAnimationFrame(() => this.map?.resize());
  }

  /**
   * Moves the camera to a chosen place.
   *
   * Zoom is set from the radius rather than held constant, so the ring fills a similar share of
   * the viewport at 10 km and at 100 km. A fixed zoom would put a 100 km circle off screen and
   * leave a 10 km one as a dot.
   */
  protected locatePlace(place: PlaceMatch): void {
    const radiusKm = this.placeStore.radiusKm();

    this.map?.flyTo({
      center: [place.longitude, place.latitude],
      zoom: Explore.zoomForRadius(radiusKm),
      duration: 1400,
      essential: true,
    });
  }

  /**
   * Moves the camera to one of the earthquakes listed for a place.
   *
   * Deliberately does not select the event: the place, its ring and its counts are the subject,
   * and opening the detail panel over them would replace the thing being read.
   */
  protected locatePlaceEvent(event: PlaceEvent): void {
    this.map?.flyTo({
      center: [event.longitude, event.latitude],
      zoom: Math.max(this.map.getZoom(), 8),
      duration: 1400,
      essential: true,
    });
  }

  /**
   * A zoom that frames a circle of the given ground radius.
   *
   * Derived from the Web Mercator resolution at the equator: at zoom z a tile spans
   * 360 / 2^z degrees, so halving the radius is one zoom level. The constant is calibrated so
   * the 25 km ring sits comfortably inside the viewport with the panel over one side.
   */
  private static zoomForRadius(radiusKm: number): number {
    return Math.min(11, Math.max(6, Math.round(11 - Math.log2(radiusKm / 10))));
  }

  protected togglePresentation(): void {
    this.presentationStore.toggle();
    requestAnimationFrame(() => this.map?.resize());
  }

  // ---- Map ----------------------------------------------------------------

  /**
   * Reflects the cross-section line onto the map.
   *
   * An effect that rebuilds the source's data whenever the endpoints change, following
   * the same pattern as the hazard layer sync: signals hold the intent, and the map is
   * brought into line with it rather than being mutated from the click handler.
   */
  private syncSectionGeometry(): void {
    const map = this.map;

    if (!map || !map.isStyleLoaded()) {
      return;
    }

    const start = this.crossSectionStore.start();
    const end = this.crossSectionStore.end();

    const features: SectionFeature[] = [];

    if (start && end) {
      features.push({
        type: 'Feature',
        properties: { role: 'line' },
        geometry: {
          type: 'LineString',
          coordinates: [
            [start.longitude, start.latitude],
            [end.longitude, end.latitude],
          ],
        },
      });
    }

    // Endpoints are drawn even before the line is complete, so the first click has
    // visible confirmation rather than appearing to do nothing.
    for (const [index, point] of [start, end].entries()) {
      if (point) {
        features.push({
          type: 'Feature',
          properties: { role: 'endpoint', label: index === 0 ? 'A' : 'B' },
          geometry: { type: 'Point', coordinates: [point.longitude, point.latitude] },
        });
      }
    }

    const source = map.getSource(Explore.sectionSourceId) as GeoJSONSource | undefined;

    source?.setData({ type: 'FeatureCollection', features } as never);
  }

  /**
   * Moves the camera to show a section line in full.
   *
   * Padded generously at the bottom because the section panel occupies the foot of the
   * map: without it the line would be framed behind the plot describing it.
   */
  private frameSection(
    start: { latitude: number; longitude: number },
    end: { latitude: number; longitude: number },
  ): void {
    this.map?.fitBounds(
      [
        [Math.min(start.longitude, end.longitude), Math.min(start.latitude, end.latitude)],
        [Math.max(start.longitude, end.longitude), Math.max(start.latitude, end.latitude)],
      ],
      {
        padding: { top: 90, right: 120, bottom: 330, left: 90 },
        duration: 1200,
      },
    );
  }

  /**
   * Reflects the comparison pair onto the map, and frames both.
   *
   * Passing null clears the geometry, so closing the comparison removes the markers without
   * a second code path.
   */
  private syncComparisonGeometry(
    endpoints: {
      left: { latitude: number; longitude: number };
      right: { latitude: number; longitude: number };
    } | null,
  ): void {
    const map = this.map;

    if (!map || !map.isStyleLoaded()) {
      return;
    }

    const source = map.getSource(Explore.comparisonSourceId) as GeoJSONSource | undefined;

    if (!endpoints) {
      source?.setData({ type: 'FeatureCollection', features: [] } as never);
      return;
    }

    const features: SectionFeature[] = [
      {
        type: 'Feature',
        properties: { role: 'line' },
        geometry: {
          type: 'LineString',
          coordinates: [
            [endpoints.left.longitude, endpoints.left.latitude],
            [endpoints.right.longitude, endpoints.right.latitude],
          ],
        },
      },
      {
        type: 'Feature',
        properties: { role: 'endpoint', label: 'A' },
        geometry: { type: 'Point', coordinates: [endpoints.left.longitude, endpoints.left.latitude] },
      },
      {
        type: 'Feature',
        properties: { role: 'endpoint', label: 'B' },
        geometry: { type: 'Point', coordinates: [endpoints.right.longitude, endpoints.right.latitude] },
      },
    ];

    source?.setData({ type: 'FeatureCollection', features } as never);

    this.frameSection(endpoints.left, endpoints.right);
  }

  /**
   * Which storm the camera has already been moved to.
   *
   * Tracked so framing happens once per storm. Without it, emphasising a different agency
   * would re-frame and pull the map out from under a reader mid-comparison.
   */
  private framedCycloneId?: string;

  /**
   * Frames a storm's full extent across every agency's track.
   *
   * Every track is included rather than only the emphasised one, so switching agency never
   * reveals a path that runs off screen.
   */
  private frameCyclone(track: CycloneTrack): void {
    const longitudes: number[] = [];
    const latitudes: number[] = [];

    for (const agency of track.tracks) {
      for (const fix of agency.fixes) {
        longitudes.push(fix.longitude);
        latitudes.push(fix.latitude);
      }
    }

    if (longitudes.length === 0) {
      return;
    }

    this.map?.fitBounds(
      [
        [Math.min(...longitudes), Math.min(...latitudes)],
        [Math.max(...longitudes), Math.max(...latitudes)],
      ],
      {
        // Left padding clears the panel; a storm track spans thousands of kilometres and
        // would otherwise be framed partly behind it.
        padding: { top: 80, right: 120, bottom: 80, left: 380 },
        duration: 1400,
      },
    );
  }

  /** Handle for the rotation animation frame, so it can be cancelled on teardown. */
  private rotationFrame?: number;

  /** Current rotation angle in degrees. Persisted across fixes so the storm never jumps. */
  private rotationDegrees = 0;

  /** When the glyph rotation was last written, for throttling. */
  private lastRotationWrite = 0;

  /**
   * Starts the rotation loop, if it is not already running.
   *
   * <b>Throttled to 10 writes a second, deliberately.</b> `setLayoutProperty` forces MapLibre to
   * recalculate the layer's layout, and calling it on every animation frame at 60 Hz was enough to
   * stall rendering — the stutter and momentary blanking that looked like a freeze. A slow
   * rotation needs nowhere near 60 steps a second: at 18 degrees per second, 10 writes gives just
   * under two degrees per step, which is below the threshold where stepping is visible.
   */
  private startRotation(): void {
    if (this.rotationFrame !== undefined) {
      return;
    }

    let last = performance.now();

    const step = (now: number) => {
      const elapsed = now - last;
      last = now;

      // 18 degrees per second: one revolution in twenty seconds. Slow enough to read as a
      // deliberate rotation rather than a spinner, which is what a fast turn would look like.
      this.rotationDegrees = (this.rotationDegrees + (elapsed / 1000) * 18) % 360;

      if (this.cycloneStore.currentFix() === null) {
        this.stopRotation();

        return;
      }

      if (now - this.lastRotationWrite >= 100) {
        this.lastRotationWrite = now;
        this.pushRotation();
      }

      this.rotationFrame = requestAnimationFrame(step);
    };

    this.rotationFrame = requestAnimationFrame(step);
  }

  private stopRotation(): void {
    if (this.rotationFrame !== undefined) {
      cancelAnimationFrame(this.rotationFrame);
      this.rotationFrame = undefined;
    }
  }

  /** Writes the rotation geometry, or clears it when there is no fix. */
  /**
   * Applies the current rotation angle to the centre glyph.
   *
   * A layout property, not geometry. The icon turns in place while its position comes from the
   * same feature the wind bands were built from, so rotation cannot pull the symbol away from the
   * data — which is exactly what happened when rotation was drawn as its own geometry in its own
   * source.
   */
  private pushRotation(): void {
    const map = this.map;

    if (!map || !map.isStyleLoaded() || !map.getLayer(Explore.cyclonePositionLayerId)) {
      return;
    }

    map.setLayoutProperty(
      Explore.cyclonePositionLayerId,
      'icon-rotate',
      // Negative: MapLibre rotates clockwise, and northern-hemisphere circulation is
      // anticlockwise.
      -this.rotationDegrees,
    );
  }

  /**
   * Registers the wind field layers and the centre glyph. Called once, after style load.
   */
  private addWindFieldLayers(map: MapLibreMap): void {
    // One glyph per intensity band, generated rather than loaded from files so they cannot fall out
    // of step with the ramp. MapLibre's `icon-color` is SDF-only and this glyph is a raster — it
    // carries a dark keyline, which an SDF cannot — so the variants are registered up front and
    // selected per feature by `icon-image`.
    for (const variant of cycloneSymbolVariants()) {
      if (map.hasImage(variant.id)) {
        continue;
      }

      const symbol = createCycloneSymbol(88, variant.colour);

      if (symbol !== null) {
        map.addImage(variant.id, symbol, { pixelRatio: 4 });
      }
    }

    map.addSource(Explore.cycloneFieldSourceId, {
      type: 'geojson',
      data: { type: 'FeatureCollection', features: [] },
    });

    // The wind surface. Each contour is shaded by the wind speed it represents, using the same ramp
    // as the track, so the footprint reads as an extension of the line rather than as decoration
    // around it — and the intensity gradient becomes visible in hue, not only in density. Opacity
    // stays low and accumulates through overlap: fourteen nested contours build a solid-feeling
    // core while every single one of them still lets the ground through.
    map.addLayer({
      id: Explore.cycloneGaleFillLayerId,
      type: 'fill',
      source: Explore.cycloneFieldSourceId,
      filter: ['==', ['get', 'role'], 'gale'],
      paint: {
        'fill-color': ['get', 'colour'],
        'fill-opacity': [
          'interpolate',
          ['linear'],
          ['coalesce', ['get', 'threshold'], 34],
          30, 0.026,
          64, 0.042,
          100, 0.058,
          160, 0.072,
        ],
      },
    });

    // Only the outermost contour carries an edge. Fourteen outlines would reinstate exactly the
    // banded look the contours exist to replace; one boundary states where the reported gale
    // radius falls and the gradient does the rest.
    map.addLayer({
      id: Explore.cycloneGaleEdgeLayerId,
      type: 'line',
      source: Explore.cycloneFieldSourceId,
      filter: [
        'all',
        ['==', ['get', 'role'], 'gale'],
        ['<=', ['coalesce', ['get', 'threshold'], 34], 34],
      ],
      paint: {
        'line-color': ['get', 'colour'],
        'line-width': 1.1,
        'line-opacity': 0.55,
      },
    });

    // The eyewall at true scale, in the storm's own intensity colour — it marks where the strongest
    // winds are, so it takes the colour of those winds. Dashed, following the convention used in
    // tropical-cyclone wind-field products, where the radius of maximum wind is distinguished from
    // the solid threshold rings.
    map.addLayer({
      id: Explore.cycloneEyewallLayerId,
      type: 'line',
      source: Explore.cycloneFieldSourceId,
      filter: ['==', ['get', 'role'], 'eyewall'],
      paint: {
        'line-color': ['get', 'colour'],
        'line-width': 1.4,
        'line-dasharray': [3, 2],
        'line-opacity': 0.9,
      },
    });

    // The centre. In the same source as the field, so the glyph, the dot and the rings can never
    // describe different moments — they did when rotation had its own source and its own frame
    // loop, which is what made the effect appear to move ahead of the position.
    map.addLayer({
      id: Explore.cyclonePositionLayerId,
      type: 'symbol',
      source: Explore.cycloneFieldSourceId,
      filter: ['==', ['get', 'role'], 'centre'],
      layout: {
        // Selected per feature, so the glyph carries intensity in colour as well as in size.
        'icon-image': [
          'coalesce',
          ['get', 'symbolImage'],
          ['literal', 'calametra-cyclone-glyph-unmeasured'],
        ],
        // Sized from the feature's own wind speed, so intensity classes read as different
        // objects. The image is generated at 4x, hence the fractional base scale.
        'icon-size': ['/', ['coalesce', ['get', 'symbolSize'], 22], 88],
        'icon-allow-overlap': true,
        'icon-ignore-placement': true,
        // Rotation is applied to the icon, not to geometry. That is the whole point: the glyph
        // turns while its position stays exactly where the data puts it.
        'icon-rotate': 0,
        'icon-rotation-alignment': 'map',
      },
    });
  }

  /**
   * Registers the cyclone track layers. Called once, after style load.
   */
  private addCycloneLayers(map: MapLibreMap): void {
    map.addSource(Explore.cycloneSourceId, {
      type: 'geojson',
      data: { type: 'FeatureCollection', features: [] },
    });

    // A dark casing beneath the coloured track. Every cartographic product that draws a line
    // over imagery does this, and it is the reason the intensity ramp stays readable over both
    // the pale Blue Marble bathymetry and the near-black vector styles: the colours are judged
    // against a constant dark edge instead of against whatever the basemap happens to be.
    map.addLayer({
      id: Explore.cycloneCasingLayerId,
      type: 'line',
      source: Explore.cycloneSourceId,
      filter: ['all', ['==', ['get', 'role'], 'segment'], ['get', 'emphasised']],
      layout: { 'line-cap': 'round', 'line-join': 'round' },
      paint: {
        'line-color': '#05070a',
        // `to-number` is required, not defensive: MapLibre's arithmetic operators assert a number
        // argument, while `["get"]` is typed as an untyped value, so the bare expression can be
        // rejected when the style is parsed.
        'line-width': ['+', ['to-number', ['get', 'width']], 2.4],
        'line-opacity': 0.6,
      },
    });

    // Un-emphasised agencies. Kept visible rather than hidden — the point of this platform is
    // that several agencies drew several different paths, and hiding the rest would present one
    // of them as *the* track. Dashed and neutral so they read as context: the dash pattern says
    // "another agency's opinion" without competing with the ramp for attention.
    map.addLayer({
      id: Explore.cycloneFaintLayerId,
      type: 'line',
      source: Explore.cycloneSourceId,
      filter: ['all', ['==', ['get', 'role'], 'segment'], ['!', ['get', 'emphasised']]],
      layout: { 'line-cap': 'butt', 'line-join': 'round' },
      paint: {
        'line-color': '#c9d1d9',
        'line-width': 1,
        'line-dasharray': [2, 2.5],
        'line-opacity': 0.3,
      },
    });

    // The track itself, coloured per segment by the wind speed reported at the fix it leaves.
    // Colour comes off the feature rather than out of a style expression so that the map and the
    // panel legend are painted from one table in `cyclone-intensity.ts` and cannot drift apart.
    map.addLayer({
      id: Explore.cycloneTrackLayerId,
      type: 'line',
      source: Explore.cycloneSourceId,
      filter: ['all', ['==', ['get', 'role'], 'segment'], ['get', 'emphasised']],
      layout: { 'line-cap': 'round', 'line-join': 'round' },
      paint: {
        'line-color': ['get', 'colour'],
        'line-width': ['get', 'width'],
        'line-opacity': 0.95,
      },
    });

    // The six-hourly observation markers. These are what turn an interpolated line back into the
    // discrete best-track it actually is: they bunch where the storm stalled, stretch where it
    // accelerated, and a gap in them is a gap in the record rather than a fast straight leg.
    // Zoom-scaled because at national extent a fixed radius would merge into the line, and at
    // basin extent it would swamp it. `["zoom"]` is outermost, as MapLibre requires.
    map.addLayer({
      id: Explore.cycloneFixLayerId,
      type: 'circle',
      source: Explore.cycloneSourceId,
      filter: ['all', ['==', ['get', 'role'], 'fix'], ['get', 'emphasised']],
      paint: {
        'circle-radius': [
          'interpolate',
          ['linear'],
          ['zoom'],
          3, ['*', ['to-number', ['get', 'radius']], 0.65],
          6, ['to-number', ['get', 'radius']],
          9, ['*', ['to-number', ['get', 'radius']], 1.5],
        ],
        'circle-color': ['get', 'colour'],
        'circle-stroke-color': '#05070a',
        'circle-stroke-width': 0.9,
        'circle-opacity': 0.98,
      },
    });

    // Landfall for the emphasised agency only, drawn as a ring around the fix rather than as a
    // replacement for it, so the marker underneath still carries the intensity at the coast.
    map.addLayer({
      id: Explore.cycloneLandfallLayerId,
      type: 'circle',
      source: Explore.cycloneSourceId,
      filter: ['all', ['==', ['get', 'role'], 'landfall'], ['get', 'emphasised']],
      paint: {
        'circle-radius': [
          'interpolate',
          ['linear'],
          ['zoom'],
          3, 5,
          9, 9,
        ],
        'circle-color': 'rgba(0,0,0,0)',
        'circle-stroke-color': '#ffffff',
        'circle-stroke-width': 1.4,
        'circle-stroke-opacity': 0.85,
      },
    });
  }

  /** Registers the comparison source and its layers. Called once, after style load. */
  private addComparisonLayers(map: MapLibreMap): void {
    map.addSource(Explore.comparisonSourceId, {
      type: 'geojson',
      data: { type: 'FeatureCollection', features: [] },
    });

    // Solid rather than dashed, distinguishing it from the cross-section line: this joins
    // two real epicentres, whereas the section line is an analytical construct.
    map.addLayer({
      id: Explore.comparisonLineLayerId,
      type: 'line',
      source: Explore.comparisonSourceId,
      filter: ['==', ['get', 'role'], 'line'],
      layout: { 'line-cap': 'round' },
      paint: {
        'line-color': '#ffffff',
        'line-width': 1.25,
        'line-opacity': 0.7,
      },
    });

    map.addLayer({
      id: Explore.comparisonMarkerLayerId,
      type: 'circle',
      source: Explore.comparisonSourceId,
      filter: ['==', ['get', 'role'], 'endpoint'],
      paint: {
        'circle-radius': 9,
        'circle-color': '#0d1117',
        'circle-stroke-color': '#ffffff',
        'circle-stroke-width': 1.5,
      },
    });

    // The letters that key the markers to the comparison table's columns.
    map.addLayer({
      id: Explore.comparisonLabelLayerId,
      type: 'symbol',
      source: Explore.comparisonSourceId,
      filter: ['==', ['get', 'role'], 'endpoint'],
      layout: {
        'text-field': ['get', 'label'],
        'text-size': 11,
        'text-font': ['Noto Sans Bold', 'Open Sans Bold'],
        'text-allow-overlap': true,
      },
      paint: {
        'text-color': '#ffffff',
      },
    });
  }

  /**
   * Registers the place ring and its centre. Called once, after style load.
   *
   * The ring is drawn because the radius figure in the panel is not self-explanatory: the
   * circle is centred on a representative point for the place rather than following its
   * boundary, and for a large municipality that difference is tens of kilometres. Showing the
   * circle is the only way a reader can see which of the two it is.
   *
   * Chrome rather than data, so it takes the interface accent and no hue from the depth ramp —
   * a coloured ring would read as a magnitude or a depth.
   */
  private addPlaceLayers(map: MapLibreMap): void {
    map.addSource(Explore.placeSourceId, {
      type: 'geojson',
      data: { type: 'FeatureCollection', features: [] },
    });

    // Barely there. The fill exists to say which side of the edge is being counted, and any
    // more opacity would dim the hypocentres the ring is drawn to frame.
    map.addLayer({
      id: Explore.placeRingFillLayerId,
      type: 'fill',
      source: Explore.placeSourceId,
      filter: ['==', ['get', 'role'], 'ring'],
      paint: {
        'fill-color': '#ffffff',
        'fill-opacity': 0.04,
      },
    });

    // Dashed, like the cross-section line and for the same reason: this is an analytical
    // boundary the reader chose, not a mapped one.
    map.addLayer({
      id: Explore.placeRingEdgeLayerId,
      type: 'line',
      source: Explore.placeSourceId,
      filter: ['==', ['get', 'role'], 'ring'],
      paint: {
        'line-color': '#ffffff',
        'line-width': 1.2,
        'line-dasharray': [4, 3],
        'line-opacity': 0.7,
      },
    });

    // A hollow ring rather than a filled pin. A pin has a tip that implies precision the
    // centroid does not have, and a filled dot at city zoom hides whatever is beneath it.
    map.addLayer({
      id: Explore.placeCentreLayerId,
      type: 'circle',
      source: Explore.placeSourceId,
      filter: ['==', ['get', 'role'], 'centre'],
      paint: {
        'circle-radius': 4.5,
        'circle-color': 'rgba(0,0,0,0)',
        'circle-stroke-color': '#ffffff',
        'circle-stroke-width': 1.6,
      },
    });
  }

  /**
   * Reflects the chosen place and its radius onto the map.
   *
   * Follows the same pattern as the section geometry: the store holds the intent and the map is
   * brought into line with it, rather than being mutated from a click handler.
   */
  private syncPlaceGeometry(): void {
    const map = this.map;

    if (!map || !map.isStyleLoaded()) {
      return;
    }

    const source = map.getSource(Explore.placeSourceId) as GeoJSONSource | undefined;

    if (!source) {
      return;
    }

    const place = this.placeStore.selected();

    // Emptied when nothing is chosen. A ring left behind with no panel to account for it is a
    // mark on the map asserting something the interface is no longer saying.
    if (!place || !this.placesOpen()) {
      source.setData({ type: 'FeatureCollection', features: [] } as never);

      return;
    }

    const centre = {
      type: 'Feature',
      properties: { role: 'centre' },
      geometry: { type: 'Point', coordinates: [place.longitude, place.latitude] },
    };

    // Drawn from the store's radius rather than from the loaded context, so choosing a place or
    // changing the radius has immediate visible confirmation instead of appearing to do nothing
    // for a round trip.
    const ring = {
      ...radiusRing(place.latitude, place.longitude, this.placeStore.radiusKm()),
      properties: { role: 'ring' },
    };

    source.setData({ type: 'FeatureCollection', features: [ring, centre] } as never);
  }

  /** Registers the section source and its two layers. Called once, after style load. */
  private addSectionLayers(map: MapLibreMap): void {
    map.addSource(Explore.sectionSourceId, {
      type: 'geojson',
      data: { type: 'FeatureCollection', features: [] },
    });

    // White, dashed, above everything. The section line is chrome, not data, so it
    // takes the interface's accent rather than a hue from the hazard palette.
    map.addLayer({
      id: Explore.sectionLineLayerId,
      type: 'line',
      source: Explore.sectionSourceId,
      filter: ['==', ['get', 'role'], 'line'],
      layout: { 'line-cap': 'round' },
      paint: {
        'line-color': '#ffffff',
        'line-width': 1.5,
        'line-dasharray': [3, 2],
        'line-opacity': 0.85,
      },
    });

    map.addLayer({
      id: Explore.sectionEndpointLayerId,
      type: 'circle',
      source: Explore.sectionSourceId,
      filter: ['==', ['get', 'role'], 'endpoint'],
      paint: {
        'circle-radius': 4,
        'circle-color': '#0d1117',
        'circle-stroke-color': '#ffffff',
        'circle-stroke-width': 1.5,
      },
    });
  }

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

    // The upstream basemap style references sprite icons it does not always ship — `circle-11`
    // among them — and MapLibre logs a console error for each one on every tile that wants it.
    // Supplying a 1x1 transparent placeholder satisfies the request without drawing anything.
    // The alternative is a console full of errors that are not this application's, which makes
    // real errors harder to see.
    map.on('styleimagemissing', (event) => {
      if (map.hasImage(event.id)) {
        return;
      }

      map.addImage(event.id, {
        width: 1,
        height: 1,
        data: new Uint8Array(4),
      });
    });

    map.on('load', () => {      this.applyBasemapPalette(map);
      this.addTerrainSource(map);
      this.addEarthquakeLayers(map);
      this.addSectionLayers(map);
      this.addComparisonLayers(map);
      this.addPlaceLayers(map);
      // Field before tracks, so the translucent footprint sits beneath the track line and the
      // moving position rather than washing over them.
      this.addWindFieldLayers(map);
      this.addCycloneLayers(map);
      // The centre glyph belongs to the field source, so it was registered with the field and
      // therefore beneath the track. Raised explicitly: the six-hourly fix markers added by
      // `addCycloneLayers` would otherwise cover the moving position during playback, and the
      // position is the one thing that must stay visible while the track animates.
      map.moveLayer(Explore.cyclonePositionLayerId);
      this.ready.set(true);

      // The earthquake archive is deliberately *not* fetched here. Nothing is plotted until the
      // reader picks a hazard, so the request is issued on that choice instead — see
      // `applyHazardToMap`.
      //
      // Only the layer catalogue loads eagerly: it is metadata for the layers panel, a few
      // kilobytes, and it describes both hazards rather than either one.
      //
      // The lens is applied once it resolves, not only on hazard selection. Otherwise a reader who
      // picks a hazard before the fetch returns loses their default overlays: `load` writes every
      // entry as hidden, which would overwrite an `applyLens` that had already run.
      void this.layerStore.load().then(() => this.layerStore.applyLens(this.activeLens()));

      // Applies whatever the reader has already chosen. Normally nothing, but the store is
      // root-provided, so a selection survives navigating away to Stories and back.
      this.applyHazardToMap(map);
    });

    // Framed once the container has settled. Calling fitBounds during `load` can
    // compute against a container that has not reached its final size, which frames
    // the country slightly wrong on first paint.
    map.once('idle', () => this.frameStudyArea(map, false));

    this.map = map;
  }

  /**
   * Frames the whole archipelago.
   *
   * `fitBounds` rather than a fixed zoom, because the correct zoom depends on the
   * viewport: a value that frames the country on a laptop crops it on a narrow window
   * and leaves it small on a wide monitor. Computing it from the actual container is
   * the only way "show me the Philippines" means the same thing everywhere.
   *
   * The bounds are padded beyond the study area and the padding is generous. The
   * archipelago is tall and narrow while most viewports are wide and short, so a
   * tight fit puts Batanes and Tawi-Tawi hard against the top and bottom edges — the
   * whole country is technically visible and still feels cropped. Trading horizontal
   * space, which is empty ocean anyway, for vertical breathing room is the right call.
   */
  private frameStudyArea(map: MapLibreMap, animate = true): void {
    map.fitBounds(
      [
        // Study area plus roughly 1.5 degrees of margin on each side.
        [114.0, 2.5],
        [129.5, 23.5],
      ],
      {
        // Clears the floating controls and leaves the coastline off the edges.
        padding: { top: 96, bottom: 120, left: 96, right: 120 },
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
  /**
   * Recolours the vector base for the selected layer.
   *
   * Paint properties are set programmatically rather than by swapping style URLs, because
   * `setStyle` discards every source and layer this component has added — the earthquake field,
   * fault geometry, cyclone tracks, section line — and each would have to be rebuilt and its
   * data refetched.
   *
   * The palette is defined here rather than read from CSS custom properties because these values
   * are consumed by WebGL, which cannot resolve them. Same constraint `DEPTH_BANDS` documents.
   */
  private applyBasemapPalette(map: MapLibreMap): void {
    // Shared with the story map, so a reader who picks Satellite sees imagery in both places. The
    // behaviour is non-obvious enough — imagery beneath the data, vector fills hidden rather than
    // replaced, because `setStyle` would discard every layer this application added — that two
    // implementations would drift.
    applyBasemap(map, this.basemapStore.selected(), {
      imagerySourceId: Explore.imagerySourceId,
      imageryLayerId: Explore.imageryLayerId,
    });
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

      // Applies the active floor to the newly loaded features. Easy to miss and previously
      // unnecessary: the floor used to default to null, so there was nothing to apply until the
      // reader touched the Time Machine. With an opening floor of M6.0 the filter has to be pushed
      // the moment the data arrives, or the map renders all 27,241 events while the readout claims
      // to be showing M6.0+.
      this.applyFilters();
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

  // ---- Hazard selection ---------------------------------------------------

  /**
   * Chooses the hazard to explore, or re-chooses it.
   *
   * Closing the other hazard's panels is part of selecting, not a side effect to be tidied later: a
   * cross-section of a typhoon is meaningless, and leaving the cyclone panel open over an earthquake
   * map would present two hazards as one view.
   */
  protected selectHazard(mode: HazardMode): void {
    // Dismissed either way. Re-picking the active hazard is a no-op on state, but leaving the panel
    // open over the map after a choice has been made would keep the reader one click from the thing
    // they just asked to see.
    this.openTool.set(null);

    if (this.hazardStore.selected() === mode) {
      return;
    }

    this.hazardStore.select(mode);
    this.closeHazardSpecificTools(mode);

    if (this.map) {
      this.applyHazardToMap(this.map);
    }
  }

  /** Dismisses whichever tools do not belong to the hazard being activated. */
  private closeHazardSpecificTools(mode: HazardMode | null): void {
    // Cleared for every switch, not just for one direction. The inspector shows a published layer's
    // attributes rather than a hazard's own data, so it is not gated on the hazard — but an
    // inspection the reader made before switching is stale context over a different subject.
    this.inspectedFeature.set(null);

    // Published overlays follow the hazard. Every catalogued layer is currently Seismic, so without
    // this the fault traces switched on for earthquakes stayed drawn under the cyclone view, and the
    // layers panel offered trenches to a reader looking at storm tracks.
    this.layerStore.applyLens(
      mode === null ? null : (HazardModeStore.options.find((o) => o.mode === mode)?.lens ?? null),
    );

    // The layers panel may now be empty — no Cyclone layers are catalogued — so it closes with the
    // hazard rather than opening onto nothing.
    if (this.openTool() === 'layers' && this.layerStore.forLens(this.activeLens()).length === 0) {
      this.openTool.set(null);
    }

    if (mode !== 'earthquakes') {
      // `close`, not `reset`. `reset` clears the drawn line but sets the phase to `awaiting-start`,
      // which leaves the tool *open* on its preset chooser — so switching to cyclones showed the
      // trench presets over a storm map. Only `close` returns the phase to `idle`.
      this.crossSectionStore.close();
      this.similarEventsStore.close();
      this.comparisonStore.close();
      // The place panel counts earthquakes and names fault traces, so it belongs to this hazard.
      // Closing also clears the ring, which is registered in the earthquake layer group and
      // would otherwise merely be hidden while the store still held a selection.
      this.placeStore.closePanel();
      this.selectedEventId.set(null);
      this.selectedDetail.set(null);

      // The Time Machine and the legend describe the earthquake record, so they close with it.
      if (this.openTool() === 'timeline' || this.openTool() === 'legend') {
        this.openTool.set(null);
      }
    }

    if (mode !== 'cyclones') {
      this.cycloneStore.closePanel();
    }
  }

  /**
   * Brings the map into line with the selected hazard.
   *
   * Layer visibility rather than adding and removing layers. The layers and their sources are
   * registered once at style load, so switching hazards is a visibility flag — no refetch, no
   * flicker, and the GPU-side magnitude and time filters survive a round trip through the cyclone
   * view. Removing and re-adding would discard both.
   */
  private applyHazardToMap(map: MapLibreMap): void {
    const showEarthquakes = this.hazardStore.isEarthquakes();
    const showCyclones = this.hazardStore.isCyclones();

    // Both groups, every time, and set from the hazard rather than toggled. Setting both means the
    // state of the map is a function of the selection alone: there is no sequence of switches that
    // can leave a layer from the hazard the reader just left still drawn.
    Explore.setLayerGroupVisible(map, Explore.earthquakeLayerIds, showEarthquakes);
    Explore.setLayerGroupVisible(map, Explore.cycloneLayerIds, showCyclones);

    if (showEarthquakes) {
      // Fetched on first selection and kept thereafter. One request for the whole archive, so
      // re-requesting it on every switch would be wasteful and visibly slower.
      if (!this.earthquakesRequested) {
        this.earthquakesRequested = true;
        void this.loadEarthquakes(map);
        void this.loadActivity();
      }

      return;
    }

    if (showCyclones) {
      // Opening the panel *is* the cyclone view: the storm list, the agency comparison and the
      // playback all live in it, and there is nothing to plot until a storm is chosen.
      this.cycloneStore.openPanel();
    }
  }

  /** Sets one hazard's layers visible or hidden, skipping any the style has not registered. */
  private static setLayerGroupVisible(
    map: MapLibreMap,
    layerIds: readonly string[],
    visible: boolean,
  ): void {
    for (const layerId of layerIds) {
      if (map.getLayer(layerId)) {
        map.setLayoutProperty(layerId, 'visibility', visible ? 'visible' : 'none');
      }
    }
  }

  // ---- Hazard layers ------------------------------------------------------

  /**
   * Brings the MapLibre style into line with the store.
   *
   * Diffing rather than rebuilding: MapLibre has no declarative "these are the layers"
   * call, and removing and re-adding every layer on each change would refetch geometry
   * and flicker. Only the difference is applied.
   */
  private async syncHazardLayers(entries: readonly LayerState[]): Promise<void> {
    const map = this.map;

    if (!map || !map.isStyleLoaded()) {
      return;
    }

    for (const entry of entries) {
      const attached = this.attachedLayers.has(entry.layer.id);

      if (entry.visible && !attached) {
        await this.attachHazardLayer(map, entry.layer);
      } else if (!entry.visible && attached) {
        this.detachHazardLayer(map, entry.layer.id);
      }
    }
  }

  private async attachHazardLayer(map: MapLibreMap, layer: HazardLayer): Promise<void> {
    // Reserved immediately so a second sync pass cannot attach the same layer twice
    // while the first is still awaiting geometry.
    this.attachedLayers.set(layer.id, []);
    this.layerStore.setLoading(layer.id, true);

    try {
      const layerIds =
        layer.deliveryMode === 'LocalVector'
          ? await this.attachVectorLayer(map, layer)
          : this.attachRasterLayer(map, layer);

      this.attachedLayers.set(layer.id, layerIds);
      this.layerStore.setLoading(layer.id, false);
    } catch {
      this.attachedLayers.delete(layer.id);
      this.layerStore.setFailed(layer.id, true);
    }
  }

  /**
   * Adds a stored vector layer — currently the GEM fault traces.
   *
   * Drawn as two line layers: a dark casing beneath and the trace on top. That is the
   * standard cartographic technique for a line over a busy background, and it is what
   * lets faults stay legible over both deep ocean and a dense cluster of markers
   * without needing a colour loud enough to compete with the depth ramp.
   */
  private async attachVectorLayer(map: MapLibreMap, layer: HazardLayer): Promise<string[]> {
    const collection = await firstValueFrom(this.api.getHazardFeatures(layer.id));

    const sourceId = `hazard-${layer.id}`;
    const casingId = `${sourceId}-casing`;
    const lineId = `${sourceId}-line`;

    if (!map.getSource(sourceId)) {
      map.addSource(sourceId, { type: 'geojson', data: collection as never });
    }

    // Inserted beneath the earthquake markers: faults are structural context and the
    // events are the subject, so the events must never be obscured by them.
    const beforeId = map.getLayer(Explore.earthquakeHaloLayerId)
      ? Explore.earthquakeHaloLayerId
      : undefined;

    map.addLayer(
      {
        id: casingId,
        type: 'line',
        source: sourceId,
        layout: { 'line-cap': 'round', 'line-join': 'round' },
        paint: {
          'line-color': FAULT_CASING_COLOUR,
          'line-width': faultWidthMultiplierExpression(faultCasingWidthExpression()) as never,
          'line-opacity': 0.85,
        },
      },
      beforeId,
    );

    map.addLayer(
      {
        id: lineId,
        type: 'line',
        source: sourceId,
        layout: { 'line-cap': 'round', 'line-join': 'round' },
        paint: {
          'line-color': faultColourExpression() as never,
          'line-width': faultWidthMultiplierExpression(faultLineWidthExpression()) as never,
          'line-opacity': 0.9,
        },
      },
      beforeId,
    );

    this.wireFaultInteraction(map, lineId, layer);

    return [casingId, lineId];
  }

  /**
   * Adds a proxied raster layer — currently the PHIVOLCS hazard imagery.
   *
   * The imagery is used exactly as the agency renders it. Restyling an authority's
   * hazard map to match our palette would misrepresent it, and we are not licensed to
   * hold the underlying geometry in any case (ADR-003).
   */
  private attachRasterLayer(map: MapLibreMap, layer: HazardLayer): string[] {
    const sourceId = `hazard-${layer.id}`;
    const rasterId = `${sourceId}-raster`;

    if (!map.getSource(sourceId)) {
      // The template and the tile size are chosen together, in a pure function that is unit-tested:
      // the two must agree or MapLibre requests the wrong tiles, and that is not a fault a map
      // makes obvious. See core/layers/hazard-tile-source.ts.
      const raster = hazardRasterSource(layer, this.config.apiBaseUrl);

      map.addSource(sourceId, {
        type: 'raster',
        tiles: [...raster.tiles],
        tileSize: raster.tileSize,
        attribution: layer.attribution,
      });
    }

    const beforeId = map.getLayer(Explore.earthquakeHaloLayerId)
      ? Explore.earthquakeHaloLayerId
      : undefined;

    map.addLayer(
      {
        id: rasterId,
        type: 'raster',
        source: sourceId,
        // Below full opacity so the coastline underneath stays readable; the overlay is
        // context, not a replacement basemap.
        paint: { 'raster-opacity': 0.85 },
      },
      beforeId,
    );

    if (layer.supportsFeatureInfo) {
      this.wireRasterInteraction(map, rasterId, layer);
    }

    return [rasterId];
  }

  private detachHazardLayer(map: MapLibreMap, layerId: string): void {
    for (const styleLayerId of this.attachedLayers.get(layerId) ?? []) {
      if (map.getLayer(styleLayerId)) {
        map.removeLayer(styleLayerId);
      }
    }

    const sourceId = `hazard-${layerId}`;

    if (map.getSource(sourceId)) {
      map.removeSource(sourceId);
    }

    this.attachedLayers.delete(layerId);
    this.inspectedFeature.set(null);
  }

  /** Click a stored fault to see the publisher's own attributes. */
  private wireFaultInteraction(map: MapLibreMap, lineId: string, layer: HazardLayer): void {
    map.on('mouseenter', lineId, () => {
      map.getCanvas().style.cursor = 'pointer';
    });

    map.on('mouseleave', lineId, () => {
      map.getCanvas().style.cursor = '';
    });

    map.on('click', lineId, (event) => {
      const properties = event.features?.[0]?.properties;

      if (!properties) {
        return;
      }

      // No round trip: for a stored layer the client already holds everything the
      // publisher provided.
      this.inspectedFeature.set({
        layerName: layer.displayName,
        displayValue: (properties['name'] as string | null) ?? null,
        attributes: {
          'Slip type': describeSlipType((properties['classification'] as string | null) ?? null),
          Identifier: String(properties['externalId'] ?? '—'),
          Source: layer.sourceAgency,
        },
      });
    });
  }

  /**
   * Click a proxied layer to ask the publisher what is there.
   *
   * A live request, because Calametra holds no geometry for a proxied layer — the only
   * way to know what a pixel represents is to ask the service that rendered it.
   */
  private wireRasterInteraction(map: MapLibreMap, rasterId: string, layer: HazardLayer): void {
    map.on('click', async (event) => {
      if (!map.getLayer(rasterId)) {
        return;
      }

      // Earthquake markers take precedence: a click that hits one is about the event.
      const markerHits = map.queryRenderedFeatures(event.point, {
        layers: [Explore.earthquakeLayerId],
      });

      if (markerHits.length > 0) {
        return;
      }

      try {
        const features = await firstValueFrom(
          this.api.identifyHazardFeature(layer.id, event.lngLat.lat, event.lngLat.lng),
        );

        this.inspectedFeature.set(features[0] ?? null);
      } catch {
        this.inspectedFeature.set(null);
      }
    });
  }

  protected clearInspectedFeature(): void {
    this.inspectedFeature.set(null);
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
      // Endpoint placement takes precedence: while the section tool is capturing, a
      // click on a marker is still a click on the map at that location.
      if (this.crossSectionStore.capturingClicks()) {
        return;
      }

      const eventId = event.features?.[0]?.properties?.['id'];

      if (typeof eventId === 'string') {
        void this.selectEvent(eventId);
      }
    });

    // Clicking empty map dismisses the panel: on a map, clicking away is the
    // natural gesture for "I'm done with that".
    map.on('click', (event) => {
      if (this.crossSectionStore.capturingClicks()) {
        this.crossSectionStore.placePoint(event.lngLat.lat, event.lngLat.lng);
        return;
      }

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

    // The similarity panel is anchored to the selected event, so it goes with it.
    this.similarEventsStore.close();

    // As does the comparison: its left side is the selected event, and leaving it open
    // would show a pair whose subject is no longer on screen.
    this.comparisonStore.close();
  }

  /** Points the similarity panel at the currently selected event. */
  protected openSimilarEvents(): void {
    const eventId = this.selectedEventId();

    if (eventId !== null) {
      this.similarEventsStore.open(eventId);
    }
  }

  /**
   * Moves the camera to a similarity match without changing the selection.
   *
   * Deliberately not a re-selection: the reference event has to stay in the detail panel
   * for the comparison to remain legible. Selecting the match would replace the very thing
   * it is being compared against.
   */
  protected selectSimilarMatch(match: SimilarEarthquake): void {
    this.map?.flyTo({
      center: [match.longitude, match.latitude],
      zoom: Math.max(this.map.getZoom(), 8),
      duration: 1400,
      essential: true,
    });
  }

  /**
   * Sets a similarity match against the event in view.
   *
   * The selected event stays on the left so the comparison reads in one direction. The
   * selection is not changed, for the same reason it is not changed when locating a match:
   * replacing the reference would remove the thing being compared against.
   */
  protected compareWithMatch(match: SimilarEarthquake): void {
    const eventId = this.selectedEventId();

    if (eventId !== null) {
      this.comparisonStore.compare(eventId, match.eventId);
    }
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
