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
  viewChildren,
} from '@angular/core';
import { Map as MapLibreMap, type GeoJSONSource } from 'maplibre-gl';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { APP_CONFIG } from '../../core/config/app-config';
import { CalametraApi } from '../../core/api/calametra-api';
import { HazardLayerStore } from '../../core/layers/hazard-layer-store';
import { Icon } from '../../shared/ui/icon/icon';
import { StoryStore } from '../../core/stories/story-store';
import type { StoryBeat } from '../../core/stories/story-model';
import { spotlightMask, spotlightRadiusKm } from '../../core/stories/story-spotlight';
import { BasemapStore } from '../../core/basemap/basemap-store';
import { applyBasemap } from '../../core/basemap/apply-basemap';
import { toCycloneGeoJson } from '../../core/visual/cyclone-track';
import type { CycloneTrack } from '../../core/api/contracts';
import { type EarthquakeDetail } from '../../core/api/contracts';
import { depthColourForKilometres, markerRadiusForMagnitude } from '../../core/visual/depth-scale';

/**
 * Guided reading of a single earthquake.
 *
 * ── Why this is a route rather than a mode of Explore ───────────────────────
 * It holds its own map. Two MapLibre instances alive at once means two WebGL contexts, which
 * the browser limits and does not reclaim promptly — the reason Explore disposes its map on
 * navigation. Making this a separate route guarantees only one exists at a time, which a
 * story panel layered over Explore could not.
 *
 * ── How scrolling drives the map ────────────────────────────────────────────
 * An IntersectionObserver reports which beat is centred, that sets the beat index on the
 * store, and effects reflect the resulting beat onto the camera, the hazard layers and the
 * reading panel. Scroll position is therefore the only source of narrative position; there is
 * no parallel camera state that could disagree with the paragraph on screen.
 */
@Component({
  selector: 'cal-story',
  standalone: true,
  imports: [Icon, RouterLink],
  templateUrl: './story.html',
  styleUrl: './story.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StoryView {
  private readonly config = inject(APP_CONFIG);
  private readonly api = inject(CalametraApi);
  private readonly store = inject(StoryStore);
  private readonly layerStore = inject(HazardLayerStore);
  private readonly basemapStore = inject(BasemapStore);
  private readonly destroyRef = inject(DestroyRef);

  private readonly mapContainer = viewChild<ElementRef<HTMLElement>>('mapContainer');
  private readonly scrollContainer = viewChild<ElementRef<HTMLElement>>('scroll');
  private readonly beatElements = viewChildren<ElementRef<HTMLElement>>('beat');

  private map?: MapLibreMap;

  /** The beat observer, replaced whenever a different story's beats are rendered. */
  private beatObserver?: IntersectionObserver;

  private static readonly epicentreSourceId = 'calametra-story-epicentres';
  private static readonly epicentreLayerId = 'calametra-story-epicentre-circles';
  private static readonly highlightSourceId = 'calametra-story-highlights';
  private static readonly spotlightSourceId = 'calametra-story-spotlight';
  private static readonly spotlightLayerId = 'calametra-story-spotlight-mask';
  private static readonly highlightRingLayerId = 'calametra-story-highlight-ring';
  private static readonly highlightLabelLayerId = 'calametra-story-highlight-label';
  private static readonly imagerySourceId = 'calametra-story-imagery';
  private static readonly imageryLayerId = 'calametra-story-imagery-raster';
  private static readonly cycloneSourceId = 'calametra-story-cyclone';
  private static readonly cycloneCasingLayerId = 'calametra-story-cyclone-casing';
  private static readonly cycloneFaintLayerId = 'calametra-story-cyclone-faint';
  private static readonly cycloneTrackLayerId = 'calametra-story-cyclone-track';
  private static readonly cycloneFixLayerId = 'calametra-story-cyclone-fix';

  /**
   * The story highlight gold, mirroring `--story-highlight`.
   *
   * Duplicated as a literal because MapLibre paint values are read by WebGL and cannot resolve a CSS
   * custom property. The token remains the single source for everything rendered in the DOM; this
   * constant exists only for the canvas, and the two are documented as a pair.
   */
  private static readonly highlightColour = '#f0b429';

  protected readonly story = this.store.story;
  protected readonly beats = this.store.beats;
  protected readonly beatIndex = this.store.beatIndex;
  protected readonly currentBeat = this.store.currentBeat;
  protected readonly progress = this.store.progress;

  protected readonly ready = signal(false);

  /** Every story, for the index shown before one is chosen. */
  protected readonly stories = this.store.stories;

  /** Opens a story. The map initialises itself once its container renders. */
  protected openStory(id: string): void {
    this.store.open(id);
  }

  /**
   * Returns to the index.
   *
   * The map is **kept**, not torn down. It lives outside the `@if (story())` branch precisely so it
   * survives this transition — one WebGL context and one style load for the whole visit rather than
   * one per story opened.
   *
   * This method used to call `map.remove()`, left over from the earlier layout where the map was
   * inside the story branch. That combination was a defect: removing the map left the container
   * element in place, so the initialising effect — which only re-runs when the `viewChild` signal
   * changes — never fired again, and the reader was returned to a black rectangle. Nothing failed
   * loudly; the map was simply gone for the rest of the session.
   *
   * So what has to be undone here is the story's *content*, not the map itself: the overlays it drew
   * and the camera it moved.
   */
  protected backToIndex(): void {
    this.beatObserver?.disconnect();
    this.beatObserver = undefined;

    this.detail.set(null);
    this.cyclone.set(null);
    this.focusUnavailable.set(null);

    // Cleared alongside the payloads they describe. Left set, the next story to focus the same
    // event would find its key already loaded and never fetch, leaving a panel that stays empty
    // for as long as the tab is open.
    this.loadedEarthquakeAgencyId = null;
    this.loadedCycloneStormId = null;

    this.clearStoryOverlays();

    // Back to the whole archipelago. The index is a list of stories across the country, so leaving
    // the camera inside the last story's province would imply the next one is about that place too.
    // Taken from the shared initial view rather than a literal, so it cannot drift from Explore's.
    this.map?.flyTo({
      center: [this.config.initialView.longitude, this.config.initialView.latitude],
      zoom: this.config.initialView.zoom,
      pitch: 0,
      bearing: 0,
      duration: 1200,
      essential: true,
    });

    this.store.close();
  }

  /**
   * Empties every source this view draws into.
   *
   * Necessary because the map now outlives a story. While it was destroyed on leaving, stale
   * geometry was impossible; now an uncleared epicentre or track would sit under the next story, or
   * under the index, asserting a subject the reader is not looking at. The spotlight matters most —
   * left behind, it dims the whole archipelago around a place nobody is reading about.
   */
  private clearStoryOverlays(): void {
    const empty = { type: 'FeatureCollection', features: [] } as never;

    for (const id of [
      StoryView.epicentreSourceId,
      StoryView.cycloneSourceId,
      StoryView.highlightSourceId,
      StoryView.spotlightSourceId,
    ]) {
      (this.map?.getSource(id) as GeoJSONSource | undefined)?.setData(empty);
    }
  }

  /** The focused event's full reading set, shown beside the map. */
  protected readonly detail = signal<EarthquakeDetail | null>(null);

  /** The focused storm, for cyclone stories. */
  protected readonly cyclone = signal<CycloneTrack | null>(null);

  /**
   * The identifier of a focus this beat asked for and could not get.
   *
   * Surfaced in the interface rather than swallowed. A beat's prose quotes figures from the event it
   * focuses, so a failed resolve leaves the reader with quantitative claims and no data behind
   * them — and that is precisely how the internal-id defect this content was re-keyed to fix stayed
   * invisible: the panel simply rendered empty and nothing said why. Naming the identifier also
   * makes the cause diagnosable at a glance, since a stale agency id is a content error while a
   * whole story failing is an API or database one.
   */
  protected readonly focusUnavailable = signal<string | null>(null);

  /**
   * The agency identifiers currently loaded, tracked separately from the loaded payloads.
   *
   * Necessary because a beat now names an event by the *agency's* id while the response carries this
   * platform's internal id, so the two are never equal and the previous
   * `this.detail()?.id !== eventId` guard would refetch on every beat change. Plain fields rather
   * than signals: nothing renders them, and making them reactive would re-run the loading effect
   * for no benefit.
   */
  private loadedEarthquakeAgencyId: string | null = null;
  private loadedCycloneStormId: string | null = null;

  /** Every agency's peak for the focused storm, so the prose can be checked against the data. */
  protected readonly cycloneReadings = computed(() => this.cyclone()?.tracks ?? []);

  protected readonly observations = computed(() => this.detail()?.observations ?? []);

  constructor() {
    // No story is opened here. Five exist now, so the route shows an index and the reader chooses —
    // the same principle as the hazard chooser on the map.
    //
    // The container is rendered unconditionally, outside the `@if (story())` branch, so the map is
    // created once per visit and survives the move between the index and a story. The effect reads
    // the `viewChild` signal rather than using `afterNextRender` because the signal is the thing that
    // reports the element's arrival; `afterNextRender` runs at construction, before the view exists.
    //
    // The `this.map !== undefined` guard means this initialises exactly once. Anything that disposes
    // the map must therefore also recreate it or leave the reader with an empty container — which is
    // what `backToIndex` used to do.
    effect(() => {
      const container = this.mapContainer();

      if (container === undefined || this.map !== undefined) {
        return;
      }

      this.initialiseMap(container.nativeElement);
    });

    // Re-observes whenever the rendered beats change, which includes opening a different story.
    effect(() => {
      const elements = this.beatElements();

      this.observeBeats(elements);
    });

    // Camera follows the beat. Guarded on `ready` so a beat set before the style loads does
    // not silently fail — the effect re-runs once the map reports itself ready.
    effect(() => {
      const beat = this.currentBeat();
      const isReady = this.ready();

      if (!isReady || !beat?.camera || !this.map) {
        return;
      }

      this.map.flyTo({
        center: [beat.camera.longitude, beat.camera.latitude],
        zoom: beat.camera.zoom,
        pitch: beat.camera.pitch ?? 0,
        bearing: beat.camera.bearing ?? 0,
        // Slow on purpose: the reader is reading, and a fast camera move competes with the
        // sentence that triggered it.
        duration: 2200,
        essential: true,
      });
    });

    // Follows the reader's base-layer choice while the story is open, so switching to Satellite in
    // one place does not leave the other map on a different earth.
    effect(() => {
      const option = this.basemapStore.selected();

      if (!this.ready() || !this.map) {
        return;
      }

      applyBasemap(this.map, option, {
        imagerySourceId: StoryView.imagerySourceId,
        imageryLayerId: StoryView.imageryLayerId,
      });
    });

    // The focused storm, fetched once per storm rather than per beat: a cyclone story focuses the
    // same track throughout. Keyed on the IBTrACS SID the beat names, not on the response's own id —
    // those are different identifier spaces and comparing them would refetch on every beat.
    effect(() => {
      const stormId = this.currentBeat()?.focusCycloneStormId;
      const isReady = this.ready();

      if (stormId === undefined || !isReady) {
        return;
      }

      if (this.loadedCycloneStormId !== stormId) {
        void this.loadCyclone(stormId);
      }
    });

    // Highlights follow the beat, and clear when a beat points at nothing. Written on every beat
    // rather than only when non-empty: leaving the previous beat's rings up would point the reader
    // at a place the current paragraph is not discussing.
    effect(() => {
      const beat = this.currentBeat();
      const isReady = this.ready();

      if (!isReady || !this.map) {
        return;
      }

      this.syncHighlights(this.map, beat ?? null);
    });

    // Hazard layers requested by the beat. Additive rather than exclusive: a story that
    // switched layers off again would fight the reader if they enabled one themselves.
    effect(() => {
      const beat = this.currentBeat();
      const layers = this.layerStore.layers();

      if (!beat?.layerNames) {
        return;
      }

      for (const name of beat.layerNames) {
        const entry = layers.find((candidate) => candidate.layer.displayName === name);

        if (entry === undefined) {
          // A rename in the seed data rather than a bug here; worth surfacing in the console
          // rather than failing silently, since the beat's prose refers to the layer.
          console.warn(`Story beat "${beat.id}" refers to an unknown hazard layer: ${name}`);
          continue;
        }

        if (!entry.visible) {
          this.layerStore.toggle(entry.layer.id);
        }
      }
    });

    // The focused event's readings, fetched once per event rather than per beat: four
    // consecutive beats focus the same event. Keyed on the agency identifier the beat names, for the
    // same reason as the storm above.
    effect(() => {
      const agencyEventId = this.currentBeat()?.focusEarthquakeAgencyId;

      if (agencyEventId === undefined) {
        return;
      }

      if (this.loadedEarthquakeAgencyId !== agencyEventId) {
        void this.loadDetail(agencyEventId);
      }
    });

    this.destroyRef.onDestroy(() => {
      // WebGL contexts are limited and not promptly reclaimed. Navigating away without this
      // leaks one per visit.
      this.map?.remove();
      this.map = undefined;
      this.store.close();
    });
  }

  protected step(delta: number): void {
    const target = this.beatIndex() + delta;
    const element = this.beatElements()[target]?.nativeElement;

    // Scrolls rather than setting the index directly, so the observer stays the single authority on
    // which beat is current. `start` rather than `center`, matching `scroll-snap-align: start` —
    // centring would land between two snap positions and the container would then correct it, which
    // reads as the panel jumping after it has settled.
    element?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  protected goTo(index: number): void {
    this.beatElements()[index]?.nativeElement.scrollIntoView({
      behavior: 'smooth',
      block: 'start',
    });
  }

  /**
   * Loads the focused earthquake by the reporting agency's identifier.
   *
   * The key is recorded before the request so two beats naming the same event cannot both fetch it,
   * and kept on failure so a 404 is not retried on every scroll.
   */
  private async loadDetail(agencyEventId: string): Promise<void> {
    this.loadedEarthquakeAgencyId = agencyEventId;

    try {
      const detail = await firstValueFrom(this.api.getEarthquakeByAgencyId(agencyEventId));

      this.detail.set(detail);
      this.focusUnavailable.set(null);
      this.syncEpicentres(detail);
    } catch {
      // A story that cannot load its subject still reads, but it says so: the beat's prose quotes
      // figures from this event, and an empty panel beside them looks like the platform holds no
      // readings rather than like the reference could not be resolved.
      this.detail.set(null);
      this.focusUnavailable.set(agencyEventId);
    }
  }

  private initialiseMap(container: HTMLElement): void {
    const map = new MapLibreMap({
      container,
      style: this.config.basemapStyleUrl,
      // Opens on the whole archipelago, because the first thing rendered is the index — a list of
      // stories from Batanes to the Celebes Sea. It was previously centred on Surigao at zoom 7,
      // which framed one story's coastline behind a list of seven.
      center: [this.config.initialView.longitude, this.config.initialView.latitude],
      zoom: this.config.initialView.zoom,
      attributionControl: false,
      // No user interaction: the story controls the camera, and a reader who panned away
      // would be looking at somewhere the prose is not describing.
      interactive: false,
    });

    map.on('load', () => {
      // The reader's base-layer choice applies here too. Previously this map was hard-wired to the
      // dark vector style, so picking Satellite in Explore changed one map and not the other.
      applyBasemap(map, this.basemapStore.selected(), {
        imagerySourceId: StoryView.imagerySourceId,
        imageryLayerId: StoryView.imageryLayerId,
      });

      this.addEpicentreLayer(map);
      this.addCycloneLayers(map);
      this.addHighlightLayers(map);
      this.ready.set(true);
      void this.layerStore.load();
    });

    this.map = map;
  }

  /**
   * Draws every agency's epicentre for the focused event.
   *
   * One mark per reading, never an average. The 2.54 km separation between the two solutions
   * for the 2017 event is a fact the third beat is about, and it is only visible if both are
   * plotted.
   */
  private syncEpicentres(detail: EarthquakeDetail): void {
    const source = this.map?.getSource(StoryView.epicentreSourceId) as GeoJSONSource | undefined;

    source?.setData({
      type: 'FeatureCollection',
      features: detail.observations.map((observation) => ({
        type: 'Feature',
        properties: {
          agency: observation.agency,
          radius: markerRadiusForMagnitude(observation.magnitude?.value ?? null),
          colour: depthColourForKilometres(
            observation.depth.kilometres ?? 0,
            observation.depth.isMeasured,
          ),
        },
        geometry: {
          type: 'Point',
          coordinates: [observation.longitude, observation.latitude],
        },
      })),
    } as never);
  }

  /**
   * The storm track layers, for cyclone stories.
   *
   * Reuses `toCycloneGeoJson` and the shared intensity ramp, so a track in a story is drawn by the
   * same code and coloured on the same scale as one on the Explore map. A second implementation
   * would be a second thing to keep in step with the ramp.
   *
   * Simpler than the Explore version deliberately: no wind field, no centre glyph, no playback. A
   * story beat is a still, and the prose is doing the narration.
   */
  private addCycloneLayers(map: MapLibreMap): void {
    map.addSource(StoryView.cycloneSourceId, {
      type: 'geojson',
      data: { type: 'FeatureCollection', features: [] },
    });

    map.addLayer({
      id: StoryView.cycloneCasingLayerId,
      type: 'line',
      source: StoryView.cycloneSourceId,
      filter: ['all', ['==', ['get', 'role'], 'segment'], ['get', 'emphasised']],
      layout: { 'line-cap': 'round', 'line-join': 'round' },
      paint: {
        'line-color': '#05070a',
        'line-width': ['+', ['to-number', ['get', 'width']], 2.4],
        'line-opacity': 0.6,
      },
    });

    // The other agencies, dashed. Present because a cyclone story's subject is often the difference
    // between agencies, and hiding them would remove the evidence.
    map.addLayer({
      id: StoryView.cycloneFaintLayerId,
      type: 'line',
      source: StoryView.cycloneSourceId,
      filter: ['all', ['==', ['get', 'role'], 'segment'], ['!', ['get', 'emphasised']]],
      layout: { 'line-cap': 'butt', 'line-join': 'round' },
      paint: {
        'line-color': '#c9d1d9',
        'line-width': 1,
        'line-dasharray': [2, 2.5],
        'line-opacity': 0.32,
      },
    });

    map.addLayer({
      id: StoryView.cycloneTrackLayerId,
      type: 'line',
      source: StoryView.cycloneSourceId,
      filter: ['all', ['==', ['get', 'role'], 'segment'], ['get', 'emphasised']],
      layout: { 'line-cap': 'round', 'line-join': 'round' },
      paint: {
        'line-color': ['get', 'colour'],
        'line-width': ['get', 'width'],
        'line-opacity': 0.95,
      },
    });

    map.addLayer({
      id: StoryView.cycloneFixLayerId,
      type: 'circle',
      source: StoryView.cycloneSourceId,
      filter: ['all', ['==', ['get', 'role'], 'fix'], ['get', 'emphasised']],
      paint: {
        'circle-radius': ['interpolate', ['linear'], ['zoom'], 4, 2.2, 8, 3.6],
        'circle-color': ['get', 'colour'],
        'circle-stroke-color': '#05070a',
        'circle-stroke-width': 0.9,
      },
    });
  }

  /** Loads a storm by its IBTrACS identifier and draws every agency's track. */
  private async loadCyclone(externalStormId: string): Promise<void> {
    this.loadedCycloneStormId = externalStormId;

    try {
      const track = await firstValueFrom(this.api.getCycloneByStormId(externalStormId));

      this.cyclone.set(track);
      this.focusUnavailable.set(null);

      const source = this.map?.getSource(StoryView.cycloneSourceId) as GeoJSONSource | undefined;

      // The agency with most fixes is emphasised, matching the Explore panel's default reasoning:
      // the most detailed reading is the most useful to lead with.
      const emphasised = [...track.tracks].sort((a, b) => b.fixes.length - a.fixes.length)[0];

      source?.setData(
        toCycloneGeoJson(track.tracks, emphasised?.sourceSlug ?? null) as never,
      );
    } catch {
      this.cyclone.set(null);
      this.focusUnavailable.set(externalStormId);
    }
  }

  private addEpicentreLayer(map: MapLibreMap): void {
    map.addSource(StoryView.epicentreSourceId, {
      type: 'geojson',
      data: { type: 'FeatureCollection', features: [] },
    });

    map.addLayer({
      id: StoryView.epicentreLayerId,
      type: 'circle',
      source: StoryView.epicentreSourceId,
      paint: {
        // Radius and colour come from the shared encoding, so a marker here means the same
        // thing as the identical marker on the Explore map.
        'circle-radius': ['get', 'radius'],
        'circle-color': ['get', 'colour'],
        'circle-opacity': 0.85,
        'circle-stroke-color': '#ffffff',
        'circle-stroke-width': 1.25,
      },
    });
  }

  /**
   * The spotlight mask and the subject markers.
   *
   * ── Why a mask, not a ring ────────────────────────────────────────────────
   * The first version drew a gold ring around the subject. A circle drawn on a map is an annotation
   * laid over cartography: it competes with the data it encloses, and at any zoom where the subject
   * has real extent the ring is the wrong size. This instead dims everywhere the beat is *not* about,
   * so the map itself carries the emphasis and nothing is drawn over the subject.
   *
   * The marker that remains is deliberately small — a filled dot and a label, no ring. It says
   * "precisely here" inside an area the mask has already established.
   */
  private addHighlightLayers(map: MapLibreMap): void {
    map.addSource(StoryView.spotlightSourceId, {
      type: 'geojson',
      data: { type: 'FeatureCollection', features: [] },
    });

    map.addSource(StoryView.highlightSourceId, {
      type: 'geojson',
      data: { type: 'FeatureCollection', features: [] },
    });

    // The mask. A world-covering polygon with a hole at the subject, so this single fill dims
    // everything outside it. Opacity is high enough to read as a deliberate focus and low enough
    // that the surrounding coastline stays legible as context.
    map.addLayer({
      id: StoryView.spotlightLayerId,
      type: 'fill',
      source: StoryView.spotlightSourceId,
      paint: {
        'fill-color': '#04070b',
        'fill-opacity': 0.62,
        'fill-opacity-transition': { duration: 700, delay: 0 },
      },
    });

    map.addLayer({
      id: StoryView.highlightRingLayerId,
      type: 'circle',
      source: StoryView.highlightSourceId,
      paint: {
        'circle-radius': ['interpolate', ['linear'], ['zoom'], 4, 3, 9, 4.5, 12, 6],
        'circle-color': StoryView.highlightColour,
        'circle-stroke-color': '#04070b',
        'circle-stroke-width': 1.25,
      },
    });

    map.addLayer({
      id: StoryView.highlightLabelLayerId,
      type: 'symbol',
      source: StoryView.highlightSourceId,
      layout: {
        'text-field': ['get', 'label'],
        'text-size': 11,
        'text-font': ['Noto Sans Medium', 'Open Sans Regular'],
        'text-offset': [0, 1.1],
        'text-anchor': 'top',
        'text-allow-overlap': false,
        'text-max-width': 12,
      },
      paint: {
        'text-color': StoryView.highlightColour,
        // A halo rather than a plate: the label has to hold over both the near-black vector styles
        // and bright satellite imagery, and a filled background would occlude the coastline the
        // label is describing.
        'text-halo-color': '#04070b',
        'text-halo-width': 1.8,
      },
    });
  }

  /**
   * Writes the beat's spotlight and markers.
   *
   * The lit radius is derived from the beat's own zoom rather than written into content, so it stays
   * a consistent size in the reader's eye at every scale — a fixed ground radius would be a pinprick
   * at national zoom and wider than the panel at city zoom.
   *
   * A beat with no camera gets no mask. Those are the wide framing beats, where there is no single
   * place to point at and dimming would only darken the whole map.
   */
  private syncHighlights(map: MapLibreMap, beat: StoryBeat | null): void {
    const spotlight = map.getSource(StoryView.spotlightSourceId) as GeoJSONSource | undefined;
    const markers = map.getSource(StoryView.highlightSourceId) as GeoJSONSource | undefined;
    const camera = beat?.camera;
    const highlights = beat?.highlights ?? [];

    // Centred on the first marker where there is one, so the lit area frames the subject rather than
    // the camera — the two differ when a beat frames wide and points at one place inside it.
    const focus = highlights[0] ?? camera;

    if (camera === undefined || focus === undefined) {
      spotlight?.setData({ type: 'FeatureCollection', features: [] } as never);
    } else {
      spotlight?.setData({
        type: 'FeatureCollection',
        features: [
          spotlightMask(
            focus.latitude,
            focus.longitude,
            spotlightRadiusKm(focus.latitude, camera.zoom),
          ),
        ],
      } as never);
    }

    markers?.setData({
      type: 'FeatureCollection',
      features: highlights.map((highlight) => ({
        type: 'Feature',
        properties: { label: highlight.label },
        geometry: {
          type: 'Point',
          coordinates: [highlight.longitude, highlight.latitude],
        },
      })),
    } as never);
  }

  /**
   * Reports the centred beat to the store.
   *
   * `rootMargin` narrows the trigger band to the middle of the viewport, so a beat becomes
   * current when it is being read rather than when it first appears at the edge.
   */
  private observeBeats(elements: readonly ElementRef<HTMLElement>[]): void {
    // Any previous observer is discarded first. Opening a second story renders a new set of beat
    // elements, and an observer still watching the old ones would keep reporting indices from a
    // story the reader has left.
    this.beatObserver?.disconnect();
    this.beatObserver = undefined;

    if (elements.length === 0) {
      return;
    }

    const observer = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (!entry.isIntersecting) {
            continue;
          }

          const index = elements.findIndex(
            (element) => element.nativeElement === entry.target,
          );

          if (index >= 0) {
            this.store.setBeat(index);
          }
        }
      },
      {
        // Rooted in the panel rather than the viewport. The beats scroll inside the panel now, so a
        // viewport-relative band would be measured against the wrong box and would report the wrong
        // beat — or none at all once the panel is narrower than the window.
        root: this.scrollContainer()?.nativeElement ?? null,
        rootMargin: '-45% 0px -45% 0px',
        threshold: 0,
      },
    );

    for (const element of elements) {
      observer.observe(element.nativeElement);
    }

    this.beatObserver = observer;
  }
}
