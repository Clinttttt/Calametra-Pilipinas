import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { CalametraApi } from '../api/calametra-api';
import { type HazardLayer } from '../api/contracts';

/** A catalogue entry plus whether the user has switched it on. */
export interface LayerState {
  readonly layer: HazardLayer;
  readonly visible: boolean;
  readonly loading: boolean;
  readonly failed: boolean;
}

/**
 * Which hazard layers exist and which are switched on.
 *
 * Separated from the map component because the two have different lifetimes and
 * different concerns. The catalogue is fetched once and outlives any particular map
 * instance; the component's job is to reflect this state onto a MapLibre style. Keeping
 * them apart means the layers panel can render and be tested without a map at all.
 *
 * The store deliberately does *not* touch MapLibre. It owns intent — "the user wants
 * active faults visible" — and the map component owns the consequence.
 */
@Injectable({ providedIn: 'root' })
export class HazardLayerStore {
  private readonly api = inject(CalametraApi);

  private readonly state = signal<readonly LayerState[]>([]);
  private readonly catalogueLoaded = signal(false);

  readonly layers = computed(() => this.state());

  readonly loaded = computed(() => this.catalogueLoaded());

  /**
   * Layers grouped by lens, in the catalogue's own sort order.
   *
   * Grouped rather than flat because the platform will carry seismic, cyclone, coastal
   * and terrain layers simultaneously, and an undifferentiated list of every hazard's
   * layers is the "visual soup" the lens concept exists to prevent.
   */
  readonly byLens = computed(() => {
    const groups = new Map<HazardLayer['lens'], LayerState[]>();

    for (const entry of this.state()) {
      const existing = groups.get(entry.layer.lens) ?? [];

      existing.push(entry);
      groups.set(entry.layer.lens, existing);
    }

    return [...groups.entries()].map(([lens, entries]) => ({ lens, entries }));
  });

  readonly visibleLayers = computed(() => this.state().filter((entry) => entry.visible));

  /**
   * The layers belonging to one lens.
   *
   * Used by the panel so it offers only overlays relevant to the hazard on screen. Returns empty for
   * a lens with nothing catalogued — `Cyclone` today — which the caller uses to hide the control
   * entirely rather than presenting an empty list.
   */
  forLens(lens: HazardLayer['lens'] | null): readonly LayerState[] {
    return lens === null ? [] : this.state().filter((entry) => entry.layer.lens === lens);
  }

  /**
   * Switches to a lens: its default layers on, every other lens off.
   *
   * Called when a hazard is opened, and with null when none is. Both directions matter — without the
   * "off" half, a fault trace switched on under earthquakes would stay drawn over a storm track,
   * which is precisely the leak this replaces.
   */
  applyLens(lens: HazardLayer['lens'] | null): void {
    this.state.update((entries) =>
      entries.map((entry) => ({
        ...entry,
        visible: entry.layer.lens === lens && entry.layer.isEnabledByDefault,
      })),
    );
  }

  /** Fetches the catalogue. Idempotent: repeated calls after success do nothing. */
  async load(): Promise<void> {
    if (this.catalogueLoaded()) {
      return;
    }

    try {
      const catalogue = await firstValueFrom(this.api.listHazardLayers());

      this.state.set(
        catalogue.map((layer) => ({
          layer,
          // Everything starts off, whatever the catalogue says. `isEnabledByDefault` is still
          // honoured — but as "on when this hazard is opened", not "on when the application starts".
          // The distinction matters because every seeded layer is currently Seismic, so the old
          // behaviour drew PHIVOLCS fault traces and GEM faults over the map before the reader had
          // chosen a hazard, and left them there under the cyclone view.
          visible: false,
          loading: false,
          failed: false,
        })),
      );

      this.catalogueLoaded.set(true);
    } catch {
      // The interceptor has already reported it. An empty catalogue degrades to "no
      // layers available" rather than breaking the map.
      this.state.set([]);
    }
  }

  toggle(layerId: string): void {
    this.state.update((entries) =>
      entries.map((entry) =>
        entry.layer.id === layerId ? { ...entry, visible: !entry.visible } : entry,
      ),
    );
  }

  setLoading(layerId: string, loading: boolean): void {
    this.state.update((entries) =>
      entries.map((entry) => (entry.layer.id === layerId ? { ...entry, loading } : entry)),
    );
  }

  setFailed(layerId: string, failed: boolean): void {
    this.state.update((entries) =>
      entries.map((entry) =>
        entry.layer.id === layerId ? { ...entry, failed, loading: false } : entry,
      ),
    );
  }
}
