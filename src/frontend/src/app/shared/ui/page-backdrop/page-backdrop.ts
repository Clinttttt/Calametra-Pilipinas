import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  effect,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { Map as MapLibreMap } from 'maplibre-gl';

import { APP_CONFIG } from '../../../core/config/app-config';
import { BasemapStore } from '../../../core/basemap/basemap-store';
import { applyBasemap } from '../../../core/basemap/apply-basemap';

/**
 * THE ARCHIPELAGO BEHIND A READING PAGE
 *
 * The pages that are read rather than flown over — Compare, Time, Events — had nothing behind them
 * but a gradient, and at full height that reads as an unlit black sheet. The obvious fix is a
 * decorative coastline, and this project refuses it: an outline drawn for effect would be a map on
 * this platform that answers to no source, which is exactly what everything else here rejects.
 *
 * So the ground is <b>the real basemap</b>, from the same service the Explore map uses, held at low
 * opacity behind the content. It is sourced, it is credited, and it changes with the reader's own base
 * layer choice — pick Satellite and the page sits on imagery of the archipelago rather than on a
 * different earth from the one they selected.
 *
 * <b>Non-interactive and deliberately unreadable as a map.</b> No pan, no zoom, no labels worth
 * squinting at: at this opacity it is a ground, and a reader who wants to interrogate geography has
 * Explore. It carries no data layers for the same reason — an earthquake dimmed to a fifth of its
 * contrast would be a figure nobody could read making a claim nobody could check.
 *
 * The host is absolutely positioned and `pointer-events: none`, so it never intercepts a click and
 * never becomes a scroll target. Each page that uses it prints the basemap credit in its own flow;
 * that is not left to this component, because a credit hidden behind the content it belongs to is not
 * attribution.
 */
@Component({
  selector: 'cal-page-backdrop',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<div class="backdrop__canvas" #canvas></div>',
  styles: `
    :host {
      position: absolute;
      inset: 0;
      z-index: 0;
      overflow: hidden;
      pointer-events: none;
      /* Low enough that body copy keeps its contrast, high enough that the coastline reads. Tuned
         against the lightest option: Satellite imagery is far brighter than the vector base, so the
         value that suits it is the one that governs. */
      opacity: var(--backdrop-opacity, 0.3);
    }

    .backdrop__canvas {
      position: absolute;
      inset: 0;
    }
  `,
})
export class PageBackdrop {
  private readonly config = inject(APP_CONFIG);
  private readonly basemaps = inject(BasemapStore);
  private readonly destroyRef = inject(DestroyRef);

  private readonly canvas = viewChild.required<ElementRef<HTMLDivElement>>('canvas');

  /**
   * How far to pull the camera back from the national view.
   *
   * A backdrop wants the archipelago to fill the frame rather than sit inside it with sea all round,
   * so it opens slightly tighter than Explore's opening zoom.
   */
  readonly zoomOffset = input(0.4);

  private readonly ready = signal(false);

  private map: MapLibreMap | null = null;

  constructor() {
    afterNextRender(() => this.initialise());

    // Follows the reader's base layer choice. Without this the page would sit on a different earth
    // from the one they selected in the shell, which is the sort of quiet inconsistency that makes an
    // interface feel assembled.
    effect(() => {
      const option = this.basemaps.selected();

      if (this.map !== null && this.ready()) {
        applyBasemap(this.map, option, {
          imagerySourceId: 'backdrop-imagery',
          imageryLayerId: 'backdrop-imagery-layer',
        });
      }
    });

    this.destroyRef.onDestroy(() => {
      this.map?.remove();
      this.map = null;
    });
  }

  private initialise(): void {
    const { initialView, basemapStyleUrl } = this.config;

    const map = new MapLibreMap({
      container: this.canvas().nativeElement,
      style: basemapStyleUrl,
      center: [initialView.longitude, initialView.latitude],
      zoom: initialView.zoom + this.zoomOffset(),
      interactive: false,
      // Credited in each page's own flow instead. MapLibre's control would sit behind the content at
      // this z-index, and an attribution nobody can read is not attribution.
      attributionControl: false,
    });

    // The upstream style asks for sprite icons it does not always ship. Same placeholder as every
    // other map here, for the same reason: other people's console errors hide ours.
    map.on('styleimagemissing', (event) => {
      if (!map.hasImage(event.id)) {
        map.addImage(event.id, { width: 1, height: 1, data: new Uint8Array(4) });
      }
    });

    map.on('load', () => {
      this.map = map;
      this.ready.set(true);

      applyBasemap(map, this.basemaps.selected(), {
        imagerySourceId: 'backdrop-imagery',
        imageryLayerId: 'backdrop-imagery-layer',
      });
    });
  }
}
