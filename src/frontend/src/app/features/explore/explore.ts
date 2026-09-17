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
import { ActivatedRoute } from '@angular/router';
import {
  Map as MapLibreMap,
  NavigationControl,
  ScaleControl,
  type GeoJSONSource,
  type PointLike,
} from 'maplibre-gl';

import { APP_CONFIG } from '../../core/config/app-config';
import { CalametraApi } from '../../core/api/calametra-api';
import { ComparisonStore } from '../../core/comparison/comparison-store';
import { CrossSectionPlot } from './cross-section/cross-section-plot';
import {
  LGU_HOVER_FILL,
  LGU_LINE_COLOUR,
  LGU_SELECTED_FILL,
  LGU_SELECTED_LINE,
} from '../../core/administrative/lgu-palette';
import { LguSelectionStore } from '../../core/administrative/lgu-selection-store';
import { LguPanel } from './panels/lgu-panel';
import { panelSideFor } from '../../core/administrative/panel-side';
import {
  LGU_BAND_LOCAL,
  LGU_BAND_REGIONAL,
  lguInteractiveAt,
  lguLineOpacityExpression,
  lguLineWidthExpression,
} from '../../core/administrative/lgu-zoom-bands';
import { LGU_SOURCE_LAYER, lguBoundarySource } from '../../core/layers/lgu-boundary-source';
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
} from '../../core/visual/fault-style';
import { HIGHLIGHT_COLOUR } from '../../core/visual/highlight-style';
import { EarthquakeFilterStore } from '../../core/earthquakes/earthquake-filter-store';
import { LguEarthquakeMapScopeStore } from '../../core/earthquakes/lgu-earthquake-map-scope-store';
import {
  containmentMapClause,
  eventMatchesEarthquakeMapView,
  type LoadedEarthquakeMapEvent,
} from '../../core/earthquakes/earthquake-map-view';
import { FilterPanel } from './panels/filter-panel';
import { FeatureInspector } from './panels/feature-inspector';
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
  DEPTH_QUALITY,
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
type OpenTool = 'hazards' | 'timeline' | 'legend' | 'layers' | 'filter' | null;

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
    LguPanel,
    FilterPanel,
    FeatureInspector,
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

  /**
   * The epicentre markers alone, separate from the rest of the seismic group.
   *
   * Separate because they are the one part of the earthquake view a reader may want out of the way:
   * 27,241 markers is the archive being honest about its own density, and it is also a lot to read a
   * hazard overlay or a fault trace through. The place ring is deliberately *not* here — it is drawn
   * only while a place is selected, and hiding the markers should not hide the search result.
   */
  private static readonly epicentreLayerIds = [
    Explore.earthquakeLayerId,
    Explore.earthquakeHaloLayerId,
  ] as const;

  private static readonly highlightSourceId = 'calametra-highlight';

  // ---- Administrative boundaries -----------------------------------------

  private static readonly lguSourceId = 'calametra-lgu-boundaries';
  private static readonly lguLineLayerId = 'calametra-lgu-line';
  private static readonly lguHoverLayerId = 'calametra-lgu-hover';
  private static readonly lguHitLayerId = 'calametra-lgu-hit';
  private static readonly lguSelectedFillLayerId = 'calametra-lgu-selected-fill';
  private static readonly lguSelectedLineLayerId = 'calametra-lgu-selected-line';

  /**
   * Layers whose features own a click before the land under them does.
   *
   * Every interactive data layer, not merely the earthquake markers. A municipality is the largest
   * thing under the pointer almost everywhere, so anything omitted here is a feature the reader can no
   * longer click once boundaries are interactive.
   *
   * The constant ids only. Fault and trench lines are registered per catalogued layer with generated
   * ids, so they are collected in {@link interactiveVectorLayerIds} as they are attached rather than
   * guessed at here.
   */
  private static higherPriorityLayerIds(): readonly string[] {
    return [
    Explore.earthquakeLayerId,
    Explore.earthquakeHaloLayerId,
    Explore.cycloneTrackLayerId,
    Explore.cyclonePositionLayerId,
    Explore.cycloneFixLayerId,
      Explore.placeCentreLayerId,
    ];
  }
  private static readonly highlightRingLayerId = 'calametra-highlight-ring';
  private static readonly highlightCentreLayerId = 'calametra-highlight-centre';
  private static readonly highlightRangeLayerId = 'calametra-highlight-range';
  private static readonly highlightRangeLabelLayerId = 'calametra-highlight-range-label';

  /** Distances the rings are drawn at, in kilometres. */
  private static readonly rangeRingsKm = [50, 100, 200] as const;
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
    ...Explore.epicentreLayerIds,
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
   * The magnitude the map opens on.
   *
   * Defined by <see cref="EarthquakeFilterStore"/>, which owns the floor, and re-exported here only
   * because the template's opening readout needs it. Two constants would eventually differ.
   */
  private static readonly openingMagnitudeFloor = EarthquakeFilterStore.openingMagnitudeFloor;

  private readonly config = inject(APP_CONFIG);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly api = inject(CalametraApi);
  private readonly layerStore = inject(HazardLayerStore);
  private readonly presentationStore = inject(PresentationStore);
  private readonly crossSectionStore = inject(CrossSectionStore);

  /**
   * Which municipality the reader has selected, and which they are pointing at.
   *
   * Deliberately not PlaceStore. ADR-005 D1 keeps containment and proximity as two concepts, and this
   * is where collapsing them would be least visible: a municipality click becoming a radius selection
   * would have every figure downstream measured from a town centre while the reader believed they had
   * asked about an administrative unit.
   */
  protected readonly lguSelection = inject(LguSelectionStore);
  protected readonly lguEarthquakeMapScope = inject(LguEarthquakeMapScopeStore);
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
   * Whether the style is ready to accept source and layer mutations.
   *
   * <b>Not `map.isStyleLoaded()`, and the difference caused a real fault.</b> That method reports
   * false while *any source* is still fetching, not merely while the style document is loading — so a
   * proxied hazard layer mid-request, which for PHIVOLCS can take nineteen seconds, made every guarded
   * sync in this component defer. Unticking that layer then waited for the very tile it was trying to
   * cancel: the layer stayed drawn, its inspector stayed open, and the map felt frozen.
   *
   * The style here is loaded exactly once — the basemap is changed by setting paint and toggling an
   * imagery source rather than by `setStyle`, precisely so this component's own layers survive — so a
   * flag set at `load` is both sufficient and immune to tile traffic.
   */
  private styleReady = false;

  /**
   * Work deferred until the style is ready, keyed so each kind defers once.
   *
   * Deferred rather than dropped: returning early is what left a layer ticked and undrawn, and a
   * place ring outliving the panel that explained it, because the pass being skipped was the one that
   * *clears*. Work that is dropped leaves the map asserting something the interface no longer says.
   */
  private readonly deferredStyleWork = new Set<string>();

  /**
   * Whether the epicentre markers are drawn.
   *
   * Reader-controlled, and on by default because the archive is what this view is for. Held here
   * rather than in `HazardLayerStore` because it is not a catalogued layer — nobody publishes it,
   * it has no attribution and no licence position, and putting it in that store would make the
   * platform's own rendering look like somebody's dataset.
   */
  readonly epicentresVisible = signal(true);

  /**
   * Whether the bulk municipality outlines are drawn.
   *
   * Separate from the selected unit on purpose. ADR-005 D6 distinguishes "draw many boundaries" from
   * "always draw that one": hiding the layer removes the mesh, and the municipality the reader selected
   * stays, because it is part of the answer rather than part of the basemap.
   */
  readonly boundariesVisible = signal(true);

  /**
   * Line ids of catalogued vector layers that respond to a click.
   *
   * Collected as they are attached because fault and trench lines are registered per catalogued layer
   * with generated ids. Without this, a municipality click would fire on top of a fault trace the reader
   * was aiming at.
   */
  private readonly interactiveVectorLayerIds = new Set<string>();

  /**
   * Where across the map the reader last selected a unit, 0 at the left edge and 1 at the right.
   *
   * Kept so the panel can take the far side and avoid covering the outline it describes.
   */
  private readonly lguClickX = signal<number | null>(null);

  /** Which side the administrative panel occupies. */
  protected readonly lguPanelSide = computed(() => panelSideFor(this.lguClickX()));

  /**
   * The map's current zoom, tracked so the layers panel can say why a ticked layer is not drawing.
   *
   * Some layers carry a publisher-measured minimum zoom, and MapLibre honours it silently — the tick
   * stays on and nothing appears, which is indistinguishable from the bug fixed earlier unless the
   * interface explains it.
   */
  protected readonly mapZoom = signal(0);

  /**
   * Origin time and magnitude of every loaded event.
   *
   * Kept so the visible count can be recomputed without re-querying the map or the
   * API as the filters move.
   */
  private loadedEvents: LoadedEarthquakeMapEvent[] = [];

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
  /**
   * The feature under the pointer, rendered by `cal-feature-inspector`.
   *
   * The attribute ordering and the ordinal-scale placement moved into that component with its
   * markup: they are how the panel presents a publisher's record, not something the map needs to
   * know.
   */
  protected readonly inspectedFeature = signal<HazardFeatureAttributes | null>(null);

  /**
   * Which catalogue layer the open inspector belongs to.
   *
   * Tracked separately because the identify response carries the layer's display name but not its id,
   * and the id is what `detachHazardLayer` matches on. Without it, switching off any layer dismissed
   * an inspector that belonged to another one.
   */
  private readonly inspectedLayerId = signal<string | null>(null);

  protected readonly eventCount = signal<number | null>(null);
  protected readonly loadFailed = signal(false);
  protected readonly activity = signal<EarthquakeActivity | null>(null);

  /** How many events are visible under the current filters. */
  protected readonly visibleCount = signal<number | null>(null);

  /** The semantic denominator for the readout: contained set while active, national archive otherwise. */
  protected readonly readoutTotalCount = computed(() => {
    const scope = this.lguEarthquakeMapScope.state();
    return scope.status === 'ready' ? scope.data.count : this.eventCount();
  });

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
  /**
   * The reader's own filter over the archive, beyond the timeline's scrub and floor.
   *
   * Held in a root store rather than here because the panel that sets it is created and destroyed
   * with the rail: component state would reset every time the panel was reopened. Read by
   * <see cref="applyFilters"/>, which is already the single place where the map's GPU-side predicate
   * is assembled, so this adds dimensions to an existing mechanism rather than a second one.
   */
  protected readonly filterStore = inject(EarthquakeFilterStore);

  /**
   * The active magnitude floor, owned by the filter store rather than by this component.
   *
   * The Time Machine's "M6.0+" control and the filter panel's magnitude row set the same field, and
   * two owners of one field is how a filter starts disagreeing with the count beside it — tick M6.0+
   * on the timeline, choose "Any" in the panel, and a component-local floor would leave the map
   * filtered while the panel said otherwise. One signal, two ways in.
   */
  private readonly magnitudeFloor = this.filterStore.minMagnitude;

  /**
   * When set, the map draws this event alone.
   *
   * Arriving from the catalogue the reader asked about one earthquake, and 27,000 neighbours are not
   * context at that moment — they are noise the reader has to find their event inside. So the archive
   * is narrowed to it, and the narrowing is stated with a control to undo it: a map showing one dot
   * while the corner reads 27,242 would be lying about what is on screen.
   */
  protected readonly isolatedEventId = signal<string | null>(null);

  /** Read-only views for the template and the Time Machine, so only this component mutates them. */
  protected readonly activeMagnitudeFloor = this.magnitudeFloor;
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
    const magnitude = floor === null ? 'M4.0+' : `M${floor.toFixed(1)}+`;

    // The span has to follow the window, not restate the archive's extent. Left as "1901–2026" while
    // a window was applied, this line would assert a span the map is not showing — the same class of
    // error as presenting a filtered count as a total.
    const fromMs = this.filterStore.fromMs();
    const toMs = this.filterStore.toMs();

    if (fromMs === null && toMs === null) {
      return `${magnitude} · 1901–2026`;
    }

    const from = fromMs === null ? '1901' : Explore.formatWindowEdge(fromMs);
    const to = toMs === null ? 'now' : Explore.formatWindowEdge(toMs);

    return `${magnitude} · ${from}–${to}`;
  });

  /**
   * A window edge, to the month.
   *
   * To the month rather than the day because the readout sits beside a five-digit count and has to
   * stay one line; the exact bounds are visible in the panel that set them.
   */
  private static formatWindowEdge(epochMs: number): string {
    return new Date(epochMs).toLocaleDateString('en-GB', {
      timeZone: 'UTC',
      month: 'short',
      year: 'numeric',
    });
  }

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

    // The reader's choice to hide the epicentres, applied the same way and for the same reason.
    effect(() => {
      this.epicentresVisible();

      const map = this.map;

      if (map) {
        this.applyEpicentreVisibility(map);
      }
    });

    // Municipality hover, selection and the bulk toggle, kept in step with the store.
    //
    // One effect for all three because they resolve to the same three MapLibre filters, and splitting
    // them would mean a hover change re-deciding the selected filter from a stale read.
    effect(() => {
      this.lguSelection.hoveredPsgc();
      this.lguSelection.selectedPsgc();
      this.boundariesVisible();

      this.applyLguState();
    });

    // Administrative containment is one more map-filter clause. Loading a replacement LGU hides the
    // previous set immediately; unavailable/failed scopes truthfully fall back to the whole archive.
    effect(() => {
      this.lguEarthquakeMapScope.state();

      if (this.map) {
        this.applyFilters();
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

      if (!map || !this.styleReady) {
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

      if (!map || !this.styleReady) {
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
      // The mark belongs to the list the reader was reading. Left behind, it would sit on the map
      // with nothing on screen explaining which earthquake it points at.
      this.clearHighlight();
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

    // A new place means the previous place's located earthquake is no longer the subject.
    this.clearHighlight();

    this.map?.flyTo({
      center: [place.longitude, place.latitude],
      zoom: Explore.zoomForRadius(radiusKm),
      duration: 1400,
      essential: true,
    });
  }

  /**
   * Moves the camera to one of the earthquakes listed for a place, and marks it.
   *
   * Deliberately does not select the event: the place, its ring and its counts are the subject,
   * and opening the detail panel over them would replace the thing being read. The mark is what
   * makes the move legible — at these zooms the epicentre is one marker among many, and a camera
   * move alone leaves the reader guessing which one was meant.
   */
  protected locatePlaceEvent(event: { readonly latitude: number; readonly longitude: number }): void {
    this.highlightPosition(event.longitude, event.latitude);

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

    if (!map || !this.styleReady) {
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

    if (!map || !this.styleReady) {
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

    if (!map || !this.styleReady || !map.getLayer(Explore.cyclonePositionLayerId)) {
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

    if (!map) {
      return;
    }

    // Deferred rather than dropped. The pass that empties the source is the one that matters here:
    // skipped, the dashed ring and its centre dot stay on the map with no panel accounting for them,
    // which is a circle asserting an analysis the reader has already dismissed.
    if (!this.styleReady) {
      this.deferStyleWork('place-geometry', () => this.syncPlaceGeometry());

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
      // Boundaries first, so every data layer added after this sits above them. A municipality
      // outline is context for the archive, not a thing drawn over it.
      this.addLguBoundaryLayers(map);
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
      this.styleReady = true;
      this.ready.set(true);
      this.mapZoom.set(map.getZoom());

      // Kept current so the layers panel can explain a layer that is switched on but below its
      // publisher-measured minimum zoom. `zoomend` rather than `zoom`: the latter fires per frame
      // during a pinch, and nothing here needs that resolution.
      map.on('zoomend', () => this.mapZoom.set(map.getZoom()));
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

      // An event named in the URL, which is how the catalogue hands one over. Read once at load
      // rather than watched: this is an entry point, and a reader who then clicks another marker
      // should not have the address bar drag them back.
      const requested = this.route.snapshot.queryParamMap.get('event');

      if (requested) {
        this.hazardStore.select('earthquakes');
        this.applyHazardToMap(map);

        // The reader asked for one earthquake, so the archive opens with the floor lifted rather than
        // at M6.0+ — otherwise a magnitude 4.7 arrives selected but filtered out of the map beneath it.
        this.filterStore.setMagnitudeRange(null, null);
        this.isolatedEventId.set(requested);
        this.applyFilters();

        void this.selectEvent(requested, true);
      }
    });

    // Framed once the container has settled. Calling fitBounds during `load` can
    // compute against a container that has not reached its final size, which frames
    // the country slightly wrong on first paint.
    //
    // Skipped entirely when the URL names an event: this fires after the detail request resolves and
    // the camera has already flown to the epicentre, so framing the country here snapped straight back
    // out — the reader saw the zoom happen and then undo itself. The check reads the parameter rather
    // than a flag set later, because `idle` can arrive before the fetch does.
    if (this.route.snapshot.queryParamMap.get('event') === null) {
      map.once('idle', () => this.frameStudyArea(map, false));
    }

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
    this.addHighlightLayers(map);
  }

  /**
   * The mark placed on one located earthquake.
   *
   * Registered with the earthquake layers and drawn above them, because its whole purpose is to
   * survive being surrounded: at national zoom an epicentre is a few pixels among thousands, so
   * flying the camera to one without marking it leaves the reader to guess which marker was meant.
   *
   * A ring and a centre dot rather than a filled shape. The event's own marker keeps its depth
   * colour and magnitude size — the encoding is the data, so the highlight must not overwrite it —
   * and a ring around it points without hiding it.
   */
  private addHighlightLayers(map: MapLibreMap): void {
    map.addSource(Explore.highlightSourceId, {
      type: 'geojson',
      data: { type: 'FeatureCollection', features: [] },
    });

    // Distance rings, drawn beneath the mark. Deliberately *not* an area of effect: how far shaking is
    // felt depends on depth, magnitude, scale and local ground, and this platform holds no attenuation
    // model or ShakeMap — a shaded footprint would be a modelled claim with nothing behind it. What
    // these are is a ruler on the map, so a reader can see that a town is inside 50 km rather than
    // guessing from the scale bar.
    map.addLayer({
      id: Explore.highlightRangeLayerId,
      type: 'line',
      source: Explore.highlightSourceId,
      filter: ['==', ['coalesce', ['get', 'role'], ''], 'range'],
      paint: {
        'line-color': HIGHLIGHT_COLOUR,
        'line-width': 1,
        'line-opacity': 0.35,
        'line-dasharray': [3, 3],
      },
    });

    map.addLayer({
      id: Explore.highlightRangeLabelLayerId,
      type: 'symbol',
      source: Explore.highlightSourceId,
      filter: ['==', ['coalesce', ['get', 'role'], ''], 'range'],
      layout: {
        'symbol-placement': 'line',
        'text-field': ['get', 'label'],
        'text-size': 10,
        'text-letter-spacing': 0.08,
        'text-keep-upright': true,
      },
      paint: {
        'text-color': HIGHLIGHT_COLOUR,
        'text-opacity': 0.6,
        'text-halo-color': '#05080c',
        'text-halo-width': 1.2,
      },
    });

    map.addLayer({
      id: Explore.highlightRingLayerId,
      type: 'circle',
      source: Explore.highlightSourceId,
      filter: ['!=', ['coalesce', ['get', 'role'], ''], 'range'],
      paint: {
        'circle-radius': ['interpolate', ['linear'], ['zoom'], 4, 11, 8, 16, 12, 22],
        'circle-color': 'transparent',
        'circle-stroke-width': 1.75,
        // The interaction accent, not the caution hue: this marks what the reader asked for rather
        // than warning about it, and caution has to keep meaning exactly one thing.
        'circle-stroke-color': HIGHLIGHT_COLOUR,
        'circle-stroke-opacity': 0.95,
      },
    });

    map.addLayer({
      id: Explore.highlightCentreLayerId,
      type: 'circle',
      source: Explore.highlightSourceId,
      filter: ['!=', ['coalesce', ['get', 'role'], ''], 'range'],
      paint: {
        'circle-radius': 2.2,
        'circle-color': HIGHLIGHT_COLOUR,
        'circle-opacity': 0.95,
      },
    });
  }

  /**
   * Marks one position, replacing any previous mark.
   *
   * @param withDistanceRings Draws 50, 100 and 200 km circles around the point.
   */
  private highlightPosition(
    longitude: number,
    latitude: number,
    withDistanceRings = false,
  ): void {
    const source = this.map?.getSource(Explore.highlightSourceId) as GeoJSONSource | undefined;

    const rings = withDistanceRings
      ? Explore.rangeRingsKm.map((km) => {
          const ring = radiusRing(latitude, longitude, km);

          return {
            ...ring,
            properties: { role: 'range', label: `${km} km` },
          };
        })
      : [];

    source?.setData({
      type: 'FeatureCollection',
      features: [
        ...rings,
        {
          type: 'Feature',
          properties: {},
          geometry: { type: 'Point', coordinates: [longitude, latitude] },
        },
      ],
    } as never);
  }

  /** Removes the mark. Called when the subject changes, so a stale mark cannot mislead. */
  private clearHighlight(): void {
    const source = this.map?.getSource(Explore.highlightSourceId) as GeoJSONSource | undefined;

    source?.setData({ type: 'FeatureCollection', features: [] });
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
        id: point.i,
        epochMs: point.t,
        magnitude: point.m,
        depthKm: point.d,
        // Same test the GeoJSON builder applies, so the count and the map agree on what "measured"
        // means rather than each deciding for itself.
        depthMeasured: point.q === DEPTH_QUALITY.measured,
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

  /** Activates server-authoritative LGU containment without changing place/radius state or camera. */
  protected exploreSelectedLguEarthquakes(): void {
    if (!this.hazardStore.isEarthquakes()) {
      this.hazardStore.select('earthquakes');
      this.closeHazardSpecificTools('earthquakes');
    }

    this.lguEarthquakeMapScope.activate();

    if (this.map) {
      this.applyHazardToMap(this.map);
      this.applyFilters();
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

    // The epicentres are a function of the hazard *and* the reader's own choice, applied after the
    // group so that hiding them cannot survive a switch to cyclones and back.
    this.applyEpicentreVisibility(map);

    // Marks belong to a subject. Switching hazard changes the subject, so a ring left pointing at an
    // earthquake would sit over a storm track explaining nothing.
    this.clearHighlight();

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

  /**
   * Applies the reader's epicentre choice, subject to the hazard being earthquakes.
   *
   * Both conditions, every time, for the reason `applyHazardToMap` records: computing visibility
   * from state rather than toggling it means no sequence of choices can leave markers drawn over a
   * storm track.
   */
  private applyEpicentreVisibility(map: MapLibreMap): void {
    Explore.setLayerGroupVisible(
      map,
      Explore.epicentreLayerIds,
      this.hazardStore.isEarthquakes() && this.epicentresVisible(),
    );
  }

  /**
   * Queues work until the style has settled, at most once per key.
   *
   * `idle` rather than `styledata`: it is the event that guarantees the style is loaded *and* the
   * tiles it needs are in place, and it fires whether the delay came from a basemap change or a
   * first paint. The action is re-invoked rather than a captured value replayed, so it reads current
   * state when it finally runs and several changes during one load collapse into one correct pass.
   */
  private deferStyleWork(key: string, action: () => void): void {
    if (this.deferredStyleWork.has(key)) {
      return;
    }

    this.deferredStyleWork.add(key);

    this.map?.once('idle', () => {
      this.deferredStyleWork.delete(key);
      action();
    });
  }

  // ---- Administrative boundaries -----------------------------------------

  /**
   * Attaches the current-LGU boundary tiles and the three states they are drawn in.
   *
   * **Three layers, not one styled three ways.** MapLibre resolves a paint property per feature, so
   * hover and selection could in principle be expressions on one layer. They are separate because the
   * selected outline must survive the bulk layer being switched off — ADR-005 D6 treats a selected unit
   * as part of the answer rather than part of the basemap, and a single layer cannot be both hidden and
   * showing one feature.
   *
   * **Filtered on the PSGC, not on feature-state.** Feature-state is per tile and is lost when a tile is
   * evicted, so a selection would silently vanish on pan. A filter on the code survives eviction, tile
   * reloads and re-imports, which is exactly why the server sends the code as the feature id.
   */
  private addLguBoundaryLayers(map: MapLibreMap): void {
    if (map.getSource(Explore.lguSourceId)) {
      return;
    }

    map.addSource(Explore.lguSourceId, {
      ...lguBoundarySource(this.config.apiBaseUrl),
    } as never);

    // The bulk outline. minzoom is the tile floor: below it nothing is served, so drawing would show
    // an empty layer with no explanation.
    map.addLayer({
      id: Explore.lguLineLayerId,
      type: 'line',
      source: Explore.lguSourceId,
      'source-layer': LGU_SOURCE_LAYER,
      minzoom: LGU_BAND_REGIONAL,
      paint: {
        'line-color': LGU_LINE_COLOUR,
        'line-width': lguLineWidthExpression() as never,
        'line-opacity': lguLineOpacityExpression() as never,
      },
    });

    // Hover. A fill rather than a heavier line: at these zooms a thicker outline reads as a selection,
    // and the reader has not selected anything yet. Only from the local band, because below it the
    // pointer covers several municipalities and highlighting one of them is a guess.
    map.addLayer({
      id: Explore.lguHoverLayerId,
      type: 'fill',
      source: Explore.lguSourceId,
      'source-layer': LGU_SOURCE_LAYER,
      minzoom: LGU_BAND_LOCAL,
      paint: {
        'fill-color': LGU_HOVER_FILL,
        'fill-opacity': 0.14,
      },
      filter: ['==', ['get', 'psgc'], ''],
    });

    // The selected unit. No minzoom: a reader who selected a municipality and then zoomed out to see
    // where it sits in the country must still see which one they chose.
    map.addLayer({
      id: Explore.lguSelectedFillLayerId,
      type: 'fill',
      source: Explore.lguSourceId,
      'source-layer': LGU_SOURCE_LAYER,
      paint: {
        'fill-color': LGU_SELECTED_FILL,
        'fill-opacity': 0.2,
      },
      filter: ['==', ['get', 'psgc'], ''],
    });

    map.addLayer({
      id: Explore.lguSelectedLineLayerId,
      type: 'line',
      source: Explore.lguSourceId,
      'source-layer': LGU_SOURCE_LAYER,
      paint: {
        'line-color': LGU_SELECTED_LINE,
        'line-width': 2,
        'line-opacity': 0.95,
      },
      filter: ['==', ['get', 'psgc'], ''],
    });

    // The hit target. Transparent, unfiltered, and the only layer the pointer handlers bind to.
    //
    // This exists because of a defect worth naming: the handlers were originally bound to the hover
    // layer, which is filtered to the hovered feature alone. MapLibre only fires a layer-scoped event for
    // a feature that layer actually renders, so a layer filtered to nothing renders nothing and no event
    // could ever fire — the interaction was dead at every zoom, not merely below the local band. A hit
    // layer renders every feature and paints none of them.
    map.addLayer({
      id: Explore.lguHitLayerId,
      type: 'fill',
      source: Explore.lguSourceId,
      'source-layer': LGU_SOURCE_LAYER,
      minzoom: LGU_BAND_LOCAL,
      paint: { 'fill-opacity': 0 },
    });

    this.wireLguInteraction(map);
  }

  /**
   * Hover and click for municipalities, both deferring to anything higher-priority.
   *
   * **Precedence is checked, not assumed.** The map already carries earthquake markers, cyclone tracks
   * and fix markers, fault and trench lines, and a proxied raster whose identify call is asynchronous. A
   * municipality is the largest thing under the pointer almost everywhere, so an unconditional listener
   * would win every click the reader meant for a marker. It therefore fires only when
   * `queryRenderedFeatures` finds none of the interactive data layers under the point — the same guard
   * `wireRasterInteraction` already uses for earthquake markers, extended to every interactive layer
   * rather than only that one.
   */
  private wireLguInteraction(map: MapLibreMap): void {
    map.on('mousemove', Explore.lguHitLayerId, (event) => {
      if (!lguInteractiveAt(map.getZoom()) || this.higherPriorityHit(map, event.point)) {
        this.lguSelection.clearHover();

        return;
      }

      const psgc = event.features?.[0]?.properties?.['psgc'];

      if (typeof psgc === 'string') {
        this.lguSelection.hover(psgc);
      }
    });

    // Leaving the outline clears hover and nothing else. A reader who has selected a municipality and
    // then moves the pointer away still has it selected, and the panel must not close under them.
    map.on('mouseleave', Explore.lguHitLayerId, () => this.lguSelection.clearHover());

    map.on('click', Explore.lguHitLayerId, (event) => {
      // The section tool takes precedence over everything: while it is capturing, a click on a
      // municipality is still a click on the map at that location.
      if (this.crossSectionStore.capturingClicks()) {
        return;
      }

      if (!lguInteractiveAt(map.getZoom()) || this.higherPriorityHit(map, event.point)) {
        return;
      }

      const properties = event.features?.[0]?.properties;

      if (properties === undefined || typeof properties['psgc'] !== 'string') {
        return;
      }

      // Replaces rather than accumulates: one municipality is selected at a time, because the panel
      // answers a question about one unit.
      //
      // Nothing here touches PlaceStore, moves the camera, or fetches place context. ADR-005 D1 keeps
      // containment and proximity apart, and this is the one place where collapsing them would be
      // easiest and least visible.
      this.lguClickX.set(event.point.x / Math.max(1, map.getCanvas().clientWidth));

      this.lguSelection.select({
        psgc: properties['psgc'],
        name: typeof properties['name'] === 'string' ? properties['name'] : properties['psgc'],
        kind: typeof properties['kind'] === 'string' ? properties['kind'] : 'Municipality',
        boundaryGeometryAreaSquareKm:
          typeof properties['area_km2'] === 'number' ? properties['area_km2'] : 0,
      });
    });
  }

  /**
   * Whether a click at this point belongs to a data feature rather than to the land under it.
   *
   * Queries only layers the style has actually registered: asking MapLibre for a layer that does not
   * exist throws, and which data layers are present depends on the hazard the reader chose.
   */
  private higherPriorityHit(map: MapLibreMap, point: PointLike): boolean {
    const candidates = [
      ...Explore.higherPriorityLayerIds(),
      ...this.interactiveVectorLayerIds,
    ].filter((layerId) => map.getLayer(layerId));

    if (candidates.length === 0) {
      return false;
    }

    return map.queryRenderedFeatures(point, { layers: candidates }).length > 0;
  }

  /**
   * Applies hover and selection to the map, and honours the bulk toggle.
   *
   * The selected layers are filtered independently of the bulk layer's visibility, which is the whole
   * point: hiding boundaries removes the mesh and keeps the answer.
   */
  private applyLguState(): void {
    const map = this.map;

    if (!map || !this.styleReady || !map.getLayer(Explore.lguLineLayerId)) {
      return;
    }

    const hovered = this.lguSelection.hoveredPsgc();
    const selected = this.lguSelection.selectedPsgc();

    map.setLayoutProperty(
      Explore.lguLineLayerId,
      'visibility',
      this.boundariesVisible() ? 'visible' : 'none',
    );

    // Hover follows the bulk layer: it is a pointing affordance for the mesh, so it has no meaning
    // once the mesh is hidden.
    map.setFilter(Explore.lguHoverLayerId, [
      '==',
      ['get', 'psgc'],
      this.boundariesVisible() && hovered !== null && hovered !== selected ? hovered : '',
    ]);

    for (const layerId of [Explore.lguSelectedFillLayerId, Explore.lguSelectedLineLayerId]) {
      map.setFilter(layerId, ['==', ['get', 'psgc'], selected ?? '']);
    }

    map.getCanvas().style.cursor = hovered === null ? '' : 'pointer';
  }

  /** Sets one hazard's layers visible or hidden, skipping any the style has not registered. */
  private static setLayerGroupVisible(    map: MapLibreMap,
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

    if (!map) {
      return;
    }

    // A tick made before the style is ready used to be dropped here, and nothing brought it back:
    // the effect only re-runs when store state changes, so the checkbox stayed on with no layer
    // drawn until the reader reloaded the page. The request is deferred instead. It is keyed on style
    // readiness alone, not on whether tiles are in flight — waiting for a slow proxied tile is what
    // made unticking a layer take twenty seconds.
    if (!this.styleReady) {
      this.deferStyleWork('hazard-layers', () => this.syncHazardLayers(this.layerStore.layers()));

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

      // A stored layer is drawn the moment its geometry is in hand, so the wait ends here. A proxied
      // one has only just begun requesting tiles, and `reportRasterProgress` owns its indicator until
      // the first tile lands.
      if (layer.deliveryMode === 'LocalVector') {
        this.layerStore.setLoading(layer.id, false);
      }
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
          'line-width': faultCasingWidthExpression() as never,
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
          'line-width': faultLineWidthExpression() as never,
          'line-opacity': 0.9,
        },
      },
      beforeId,
    );

    this.wireFaultInteraction(map, lineId, layer);

    // Registered so a municipality click cannot fire on top of a fault trace the reader was aiming at.
    this.interactiveVectorLayerIds.add(lineId);

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
        // Honoured by MapLibre by not issuing the request at all, which is the point: a layer below
        // its minimum costs the publisher nothing rather than costing it a national render. Null for
        // most layers, and spread rather than set to 0 so "no limit" stays absent from the style.
        ...(layer.minimumZoom === null ? {} : { minzoom: layer.minimumZoom }),
        // Below full opacity so the coastline underneath stays readable; the overlay is
        // context, not a replacement basemap.
        paint: { 'raster-opacity': 0.85 },
      },
      beforeId,
    );

    if (layer.supportsFeatureInfo) {
      this.wireRasterInteraction(map, rasterId, layer);
    }

    this.reportRasterProgress(map, sourceId, layer.id);

    return [rasterId];
  }

  /**
   * Keeps a proxied layer's row in a loading state until its first tile actually arrives.
   *
   * The panel used to clear the indicator as soon as the layer was added to the style, which is when
   * the *request* starts, not when anything is drawn. For MGB's cached layers that is a fifth of a
   * second and the distinction does not matter; for PHIVOLCS, which renders every tile on demand,
   * nineteen seconds can pass with a ticked box, an empty map and no indication that anything is
   * happening. That is indistinguishable from the layer being broken, so the wait is reported.
   *
   * Removed on the first `isSourceLoaded` rather than left attached: subsequent pans fetch more tiles,
   * and a row that flickered into loading on every pan would be noise.
   */
  private reportRasterProgress(map: MapLibreMap, sourceId: string, layerId: string): void {
    this.layerStore.setLoading(layerId, true);

    const onSourceData = (event: { sourceId?: string; isSourceLoaded?: boolean }): void => {
      if (event.sourceId !== sourceId || !event.isSourceLoaded) {
        return;
      }

      this.layerStore.setLoading(layerId, false);
      map.off('sourcedata', onSourceData);
    };

    map.on('sourcedata', onSourceData);
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

    // Only if the open inspector belongs to *this* layer. Clearing unconditionally would dismiss a
    // fault's attributes because an unrelated susceptibility layer was switched off.
    if (this.inspectedLayerId() === layerId) {
      this.inspectedFeature.set(null);
      this.inspectedLayerId.set(null);
      this.clearHighlight();
    }
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
      this.inspectedLayerId.set(layer.id);
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

        const inspected = features[0] ?? null;

        this.inspectedLayerId.set(inspected === null ? null : layer.id);
        this.inspectedFeature.set(inspected);

        // Marks where the reading was taken. On a national polygon fill every part of a class looks
        // alike, so a panel saying "High Potential" with nothing on the map leaves the reader unsure
        // which of several patches they hit.
        //
        // The clicked point, deliberately, and not the polygon: the outline would have to come from
        // the agency's own vector geometry, which ADR-003 refuses to take. The honest mark is where
        // the question was asked.
        if (inspected) {
          this.highlightPosition(event.lngLat.lng, event.lngLat.lat);
        } else {
          this.clearHighlight();
        }
      } catch {
        this.inspectedFeature.set(null);
        this.clearHighlight();
      }
    });
  }

  protected clearInspectedFeature(): void {
    this.inspectedFeature.set(null);
    this.clearHighlight();
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
    const filter = this.filterStore.filter();
    const clauses: unknown[] = [];
    const containmentScope = this.lguEarthquakeMapScope.state();

    const containmentClause = containmentMapClause(containmentScope);

    if (containmentClause !== null) {
      clauses.push(containmentClause);
    }

    // Isolation composes with containment and every reader-set filter. Naming an event never grants it
    // membership in an LGU it is outside, and entering containment never clears an existing isolation.
    const isolated = this.isolatedEventId();

    if (isolated !== null) {
      clauses.push(['==', ['get', 'id'], isolated]);
    }

    if (instantMs !== null) {
      clauses.push(['<=', ['get', 'epochMs'], instantMs]);
    }

    // The reader's window. Both edges are applied, and the upper edge coexists with the scrubber's
    // instant rather than replacing it — two upper bounds intersect, which is what a reader
    // scrubbing inside a chosen window expects.
    if (filter.fromMs !== null) {
      clauses.push(['>=', ['get', 'epochMs'], filter.fromMs]);
    }

    if (filter.toMs !== null) {
      clauses.push(['<=', ['get', 'epochMs'], filter.toMs]);
    }

    // Events with no reported magnitude are excluded from a magnitude bound rather than treated as
    // zero: an unmeasured magnitude is not evidence of a small earthquake.
    if (filter.minMagnitude !== null) {
      clauses.push(['>=', ['coalesce', ['get', 'magnitude'], -1], filter.minMagnitude]);
    }

    if (filter.maxMagnitude !== null) {
      clauses.push(['<=', ['coalesce', ['get', 'magnitude'], 99], filter.maxMagnitude]);
    }

    // Depth bounds use the same convention: an event with no depth at all is outside any depth
    // range rather than at its surface.
    if (filter.minDepthKm !== null) {
      clauses.push(['>=', ['coalesce', ['get', 'depthKm'], -1], filter.minDepthKm]);
    }

    if (filter.maxDepthKm !== null) {
      clauses.push(['<=', ['coalesce', ['get', 'depthKm'], 9999], filter.maxDepthKm]);
    }

    if (!filter.includeAssignedDepth) {
      // 43% of the archive carries a depth the agency assigned rather than measured. Left in by
      // default and disclosed; taken out only when the reader asks, because a fixed 33 km is not a
      // measurement and a depth study should be able to exclude it.
      clauses.push(['coalesce', ['get', 'depthMeasured'], false]);
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
    const filter = this.filterStore.filter();
    const isolated = this.isolatedEventId();
    const containmentScope = this.lguEarthquakeMapScope.state();
    const containedIds = containmentScope.status === 'ready'
      ? new Set(containmentScope.data.points.map((point) => point.i))
      : null;

    if (instantMs === null && !this.filterStore.isFiltered() && isolated === null) {
      this.visibleCount.set(null);
      this.lguEarthquakeMapScope.setShownCount(
        containmentScope.status === 'ready' ? containmentScope.data.count : null,
      );

      return;
    }

    // Counted from the loaded event list rather than from rendered features:
    // queryRenderedFeatures only sees the current viewport, which would make the
    // count change as the user pans.
    //
    // The predicate deliberately mirrors the paint filter clause for clause, including the
    // treatment of an absent magnitude or depth. A count that disagreed with the map would be worse
    // than no count: the figure in the corner is what a reader quotes.
    const visible = this.loadedEvents.filter((event) =>
      eventMatchesEarthquakeMapView(event, filter, instantMs, isolated, containedIds),
    ).length;

    this.visibleCount.set(visible);
    this.lguEarthquakeMapScope.setShownCount(
      containmentScope.status === 'ready' ? visible : null,
    );
  }

  protected onInstantChanged(instantMs: number | null): void {
    this.timeInstantMs.set(instantMs);
    this.applyFilters();
  }

  /**
   * Re-applies the map's predicate after the filter panel changes a bound.
   *
   * An explicit call rather than an effect on the store, matching how the Time Machine's own changes
   * arrive. The predicate is assembled in one place and pushed; nothing watches the store, so there
   * is no second route by which the map and the count could fall out of step.
   */
  protected onFilterChanged(): void {
    this.applyFilters();
  }

  /**
   * Zooms to the scale a layer's publisher can serve, keeping the centre.
   *
   * The reader asked for a layer and was told to come closer; taking them there is the useful
   * response. The centre is kept because they were already looking at the area they care about.
   */
  protected zoomTo(zoom: number): void {
    this.map?.easeTo({ zoom, duration: 900, essential: true });
  }

  /** Returns the whole archive to the map, keeping the event selected. */
  protected showWholeArchive(): void {
    this.isolatedEventId.set(null);
    this.applyFilters();
  }

  protected onMagnitudeFloorChanged(floor: number | null): void {
    // Written to the store, not to a local signal: the filter panel reads the same field.
    this.filterStore.setMagnitudeRange(floor, this.filterStore.maxMagnitude());
    this.applyFilters();  }

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
        // Marked as well as selected. At these densities the clicked marker is one of dozens under
        // the pointer's neighbourhood, and a panel opening on the right with nothing changed on the
        // map leaves the reader unsure which dot they hit. The mark is placed on the feature's own
        // coordinate rather than the click position, so it lands on the epicentre and not a pixel
        // beside it.
        const [longitude, latitude] = (
          event.features?.[0]?.geometry as { coordinates?: [number, number] } | undefined
        )?.coordinates ?? [event.lngLat.lng, event.lngLat.lat];

        this.highlightPosition(longitude, latitude);

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
  private async selectEvent(eventId: string, focus = false): Promise<void> {
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

        // Arriving from the catalogue, the reader asked for one earthquake and would otherwise land on
        // the whole archive at national zoom with a panel open somewhere to the right. So the camera
        // goes to it and the mark says which one — but only when the event was named in the URL. A
        // marker click already happened where the reader was looking, and moving the map under them
        // would be disorienting.
        if (focus) {
          this.highlightPosition(detail.longitude, detail.latitude, true);

          this.map?.flyTo({
            center: [detail.longitude, detail.latitude],
            // Close enough that the epicentre and its neighbours are distinguishable, without
            // implying the location is more precise than the catalogue's own coordinate.
            zoom: 8.5,
            duration: 1600,
            essential: true,
          });
        }
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

    // Dismissing the event dismisses the narrowing that came with it: a map showing one dot with no
    // panel to account for it is worse than either.
    if (this.isolatedEventId() !== null) {
      this.isolatedEventId.set(null);
      this.applyFilters();
    }

    // The mark goes with the selection it was pointing at.
    this.clearHighlight();

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
