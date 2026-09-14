import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { LngLatBounds, Map as MapLibreMap, type GeoJSONSource } from 'maplibre-gl';

import { APP_CONFIG } from '../../core/config/app-config';
import { FAULT_CASING_COLOUR, faultColourExpression } from '../../core/visual/fault-style';
import {
  UNMEASURED_DEPTH_COLOUR,
  depthColourForKilometres,
  markerRadiusForMagnitude,
} from '../../core/visual/depth-scale';
import { radiusRing } from '../../core/places/radius-ring';
import type { HazardFeatureCollection } from '../../core/api/contracts';

/** One plotted earthquake, reduced to what a circle needs. */
export interface LocatorEvent {
  readonly latitude: number;
  readonly longitude: number;
  readonly magnitude: number | null;
  readonly depthKm: number | null;
  readonly depthMeasured: boolean;
}

/**
 * A LOCATOR FIGURE, NOT AN INSTRUMENT
 *
 * Two of these sit side by side on the Compare page, and the whole point is that they are drawn at
 * <b>the same scale</b>. `fitBounds` on the radius ring is what guarantees it: the ring for a given
 * radius spans the same number of degrees of latitude anywhere in the archipelago, so fitting that
 * box to a container of fixed height yields identical metres-per-pixel on both sides. Setting a
 * matching `zoom` number would <em>not</em> — a degree of longitude is 111 km at Tawi-Tawi and 105 km
 * at Batanes, so the same zoom draws two different scales and the circles would differ in size while
 * claiming the same radius.
 *
 * <b>Deliberately non-interactive.</b> No pan, no zoom, no navigation control. A reader who wants to
 * interrogate the map has Explore, which is built for it; here the map is a figure whose job is to
 * make a count spatially legible, and a figure that can be dragged out of alignment stops comparing.
 *
 * <b>What it draws, and why only this.</b> The radius ring, the place's representative point, the
 * mapped fault traces, and the M6.0+ earthquakes — which is exactly the series the headline count
 * beside it reports, so the map and the number make the same claim rather than two different ones.
 * The full catalogue is not plotted: at 100 km it is up to about 1,800 points whose density is a
 * record of detection history as much as of seismicity, and drawing it here would restate the one
 * comparison this page refuses to make.
 */
@Component({
  selector: 'cal-locator-map',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="locator__canvas" #canvas></div>
    @if (!ready()) {
      <span class="locator__pending c-label">Drawing…</span>
    }
  `,
  styles: `
    :host {
      position: relative;
      display: block;
      block-size: var(--locator-height, 13rem);
      border: 1px solid var(--line-subtle);
      border-radius: var(--radius-sm);
      overflow: hidden;
      background: var(--surface-100);
    }

    .locator__canvas {
      position: absolute;
      inset: 0;
    }

    .locator__pending {
      position: absolute;
      inset-block-end: var(--space-2);
      inset-inline-start: var(--space-2);
      color: var(--text-tertiary);
    }
  `,
})
export class LocatorMap {
  private readonly config = inject(APP_CONFIG);
  private readonly destroyRef = inject(DestroyRef);

  private readonly canvas = viewChild.required<ElementRef<HTMLDivElement>>('canvas');

  readonly latitude = input.required<number>();
  readonly longitude = input.required<number>();
  readonly radiusKm = input.required<number>();

  /** The comparable series — M6.0+ — plotted as circles sized by magnitude and coloured by depth. */
  readonly events = input<readonly LocatorEvent[]>([]);

  /** GEM fault traces. Shared between both maps: 155 traces fetched once, filtered by neither. */
  readonly faults = input<HazardFeatureCollection | null>(null);

  protected readonly ready = signal(false);

  private map: MapLibreMap | null = null;

  private static readonly ringSourceId = 'locator-ring';
  private static readonly markSourceId = 'locator-mark';
  private static readonly eventsSourceId = 'locator-events';
  private static readonly faultsSourceId = 'locator-faults';

  /** The ring recomputed whenever the place or the radius changes; also the camera's target. */
  private readonly ring = computed(() =>
    radiusRing(this.latitude(), this.longitude(), this.radiusKm()),
  );

  private readonly eventCollection = computed(() => ({
    type: 'FeatureCollection' as const,
    features: this.events().map((event) => ({
      type: 'Feature' as const,
      // Radius and colour are resolved here rather than in a paint expression so the ramp stays
      // defined once, in `depth-scale.ts`, and the figure cannot drift from the map in Explore.
      // Scaled to two thirds: this canvas is a fifth of the width Explore's markers were sized for.
      properties: {
        radius: Math.max(2.5, markerRadiusForMagnitude(event.magnitude) * 0.66),
        colour: depthColourForKilometres(event.depthKm ?? 0, event.depthMeasured),
        assigned: event.depthMeasured ? 0 : 1,
      },
      geometry: { type: 'Point' as const, coordinates: [event.longitude, event.latitude] },
    })),
  }));

  constructor() {
    afterNextRender(() => this.initialise());

    // One effect for all four inputs: MapLibre wants the source replaced wholesale anyway, and
    // splitting it would risk a half-updated figure between two change detections.
    effect(() => {
      const ring = this.ring();
      const events = this.eventCollection();
      const faults = this.faults();
      const map = this.map;

      if (map === null || !this.ready()) {
        return;
      }

      (map.getSource(LocatorMap.ringSourceId) as GeoJSONSource | undefined)?.setData(ring as never);

      (map.getSource(LocatorMap.markSourceId) as GeoJSONSource | undefined)?.setData({
        type: 'Feature',
        properties: {},
        geometry: { type: 'Point', coordinates: [this.longitude(), this.latitude()] },
      } as never);

      (map.getSource(LocatorMap.eventsSourceId) as GeoJSONSource | undefined)?.setData(
        events as never,
      );

      (map.getSource(LocatorMap.faultsSourceId) as GeoJSONSource | undefined)?.setData(
        (faults ?? { type: 'FeatureCollection', features: [] }) as never,
      );

      this.frame();
    });

    this.destroyRef.onDestroy(() => {
      this.map?.remove();
      this.map = null;
    });
  }

  private initialise(): void {
    const map = new MapLibreMap({
      container: this.canvas().nativeElement,
      style: this.config.basemapStyleUrl,
      center: [this.longitude(), this.latitude()],
      zoom: 6,
      interactive: false,
      attributionControl: { compact: true },
    });

    // The upstream style asks for sprite icons it does not always ship. Same placeholder as the
    // Explore map, for the same reason: a console full of other people's errors hides ours.
    map.on('styleimagemissing', (event) => {
      if (!map.hasImage(event.id)) {
        map.addImage(event.id, { width: 1, height: 1, data: new Uint8Array(4) });
      }
    });

    map.on('load', () => {
      this.addLayers(map);
      this.map = map;
      this.ready.set(true);
      this.frame();
    });
  }

  /**
   * Fits the ring to the container.
   *
   * `padding` in pixels rather than a fraction, so both maps leave the same margin and the circles
   * come out the same size. `duration: 0` because this is a figure being drawn, not a camera move a
   * reader needs to follow — the slow flights belong to Explore.
   */
  private frame(): void {
    const map = this.map;

    if (map === null) {
      return;
    }

    const bounds = new LngLatBounds();

    for (const [longitude, latitude] of this.ring().geometry.coordinates[0]) {
      bounds.extend([longitude, latitude]);
    }

    map.fitBounds(bounds, { padding: 14, duration: 0, animate: false });
  }

  private addLayers(map: MapLibreMap): void {
    map.addSource(LocatorMap.faultsSourceId, {
      type: 'geojson',
      data: { type: 'FeatureCollection', features: [] },
    });

    map.addSource(LocatorMap.ringSourceId, { type: 'geojson', data: this.ring() as never });

    map.addSource(LocatorMap.eventsSourceId, {
      type: 'geojson',
      data: this.eventCollection() as never,
    });

    map.addSource(LocatorMap.markSourceId, {
      type: 'geojson',
      data: {
        type: 'Feature',
        properties: {},
        geometry: { type: 'Point', coordinates: [this.longitude(), this.latitude()] },
      } as never,
    });

    // Casing then trace: the standard technique for a line over a busy ground, and what keeps a
    // fault legible over both deep ocean and a cluster of markers without a hue loud enough to
    // compete with the depth ramp. Colours come from `fault-style.ts`, the same definitions the
    // Explore map paints from, so a trace cannot look like one thing here and another there.
    map.addLayer({
      id: 'locator-faults-casing',
      type: 'line',
      source: LocatorMap.faultsSourceId,
      paint: { 'line-color': FAULT_CASING_COLOUR, 'line-width': 2.4, 'line-opacity': 0.8 },
    });

    map.addLayer({
      id: 'locator-faults-line',
      type: 'line',
      source: LocatorMap.faultsSourceId,
      paint: {
        'line-color': faultColourExpression() as never,
        'line-width': 0.9,
        'line-opacity': 0.75,
      },
    });

    map.addLayer({
      id: 'locator-ring-fill',
      type: 'fill',
      source: LocatorMap.ringSourceId,
      paint: { 'fill-color': '#ffffff', 'fill-opacity': 0.04 },
    });

    map.addLayer({
      id: 'locator-ring-line',
      type: 'line',
      source: LocatorMap.ringSourceId,
      paint: {
        'line-color': '#ffffff',
        'line-width': 1,
        'line-opacity': 0.4,
        'line-dasharray': [3, 3],
      },
    });

    // An unmeasured depth is marked, not merely coloured: the ring is the same disclosure the
    // Explore map draws, and 43% of the archive needs it.
    map.addLayer({
      id: 'locator-events-assigned',
      type: 'circle',
      source: LocatorMap.eventsSourceId,
      filter: ['==', ['get', 'assigned'], 1],
      paint: {
        'circle-radius': ['+', ['get', 'radius'], 2],
        'circle-color': 'transparent',
        'circle-stroke-color': UNMEASURED_DEPTH_COLOUR,
        'circle-stroke-width': 0.9,
        'circle-stroke-opacity': 0.9,
      },
    });

    map.addLayer({
      id: 'locator-events',
      type: 'circle',
      source: LocatorMap.eventsSourceId,
      paint: {
        'circle-radius': ['get', 'radius'],
        'circle-color': ['get', 'colour'],
        'circle-opacity': 0.85,
        'circle-stroke-color': FAULT_CASING_COLOUR,
        'circle-stroke-width': 0.5,
      },
    });

    map.addLayer({
      id: 'locator-mark-halo',
      type: 'circle',
      source: LocatorMap.markSourceId,
      paint: {
        'circle-radius': 5,
        'circle-color': 'transparent',
        'circle-stroke-color': '#ffffff',
        'circle-stroke-width': 1.25,
      },
    });

    map.addLayer({
      id: 'locator-mark',
      type: 'circle',
      source: LocatorMap.markSourceId,
      paint: { 'circle-radius': 1.75, 'circle-color': '#ffffff' },
    });
  }
}
