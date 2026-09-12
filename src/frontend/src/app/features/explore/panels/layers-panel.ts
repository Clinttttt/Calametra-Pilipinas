import { ChangeDetectionStrategy, Component, computed, inject, model, output, signal } from '@angular/core';

import { HazardLayerStore } from '../../../core/layers/hazard-layer-store';
import { HazardModeStore } from '../../../core/hazards/hazard-mode-store';
import { Icon } from '../../../shared/ui/icon/icon';
import { type HazardLayer } from '../../../core/api/contracts';
import { type IconName } from '../../../shared/ui/icon/icon-paths';

/** Icon per lens, so a group is identifiable at a glance. */
const LENS_ICONS: Readonly<Record<HazardLayer['lens'], IconName>> = {
  Seismic: 'lens-seismic',
  Coastal: 'lens-coastal',
  Terrain: 'lens-terrain',
  Cyclone: 'lens-cyclone',
  Exposure: 'lens-exposure',
  History: 'lens-history',
};

/**
 * Hazard layer controls, grouped by lens.
 *
 * Grouped rather than flat because the platform will carry seismic, cyclone, coastal and
 * terrain layers simultaneously. A single undifferentiated list of every hazard's layers
 * is exactly the state the lens concept exists to prevent.
 *
 * Each layer states how it is delivered. That is not incidental metadata: a proxied
 * layer is the publisher's own rendered imagery that Calametra does not hold, while a
 * stored layer is geometry Calametra is licensed to keep and can query. The user is
 * entitled to know which they are looking at, and it explains why one is inspectable
 * differently from the other.
 */
@Component({
  selector: 'cal-layers-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  templateUrl: './layers-panel.html',
  styleUrl: './layers-panel.scss',
})
export class LayersPanel {
  private readonly store = inject(HazardLayerStore);
  private readonly hazardStore = inject(HazardModeStore);

  readonly closed = output<void>();

  /**
   * Whether the epicentre markers are drawn.
   *
   * A two-way binding to the map component rather than a store entry: this is the platform's own
   * rendering of the earthquake archive, not a catalogued layer with a publisher, an attribution and
   * a licence position, and listing it in `HazardLayerStore` would blur that distinction. It is
   * offered here because this panel is where a reader already comes to decide what is drawn.
   */
  readonly epicentresVisible = model(true);

  /** Shown only under the earthquake view, where the markers exist to be hidden. */
  protected readonly showsEpicentreControl = computed(
    () => this.hazardStore.active()?.lens === 'Seismic',
  );

  /**
   * Layer groups for the hazard on screen, not the whole catalogue.
   *
   * The panel used to list every lens. With only Seismic layers catalogued that meant a reader
   * looking at cyclones was offered PHIVOLCS fault traces and trenches — earthquake overlays under a
   * storm map. Grouping by lens is retained rather than flattened, because it still labels what the
   * reader is switching on and the catalogue will carry several lenses per hazard in time.
   */
  protected readonly groups = computed(() => {
    const lens = this.hazardStore.active()?.lens ?? null;

    return this.store.byLens().filter((group) => group.lens === lens);
  });

  protected readonly loaded = this.store.loaded;

  /**
   * Which layers have their explanatory detail expanded.
   *
   * Collapsed by default. Every layer carries a caveat, and rendering all of them at
   * once turned the panel into a column of amber blocks that buried the controls it
   * exists for. The caveats still matter — they are how the platform stays honest about
   * what a layer does and does not mean — so they are one click away rather than gone.
   */
  private readonly expanded = signal<ReadonlySet<string>>(new Set());

  protected lensIcon(lens: HazardLayer['lens']): IconName {
    return LENS_ICONS[lens];
  }

  /** Human wording for delivery mode, since the enum value is not reader-facing. */
  protected deliveryLabel(layer: HazardLayer): string {
    return layer.deliveryMode === 'RemoteWms' ? 'proxied' : 'stored';
  }

  protected isExpanded(layerId: string): boolean {
    return this.expanded().has(layerId);
  }

  protected toggleNote(layerId: string): void {
    this.expanded.update((current) => {
      const next = new Set(current);

      if (!next.delete(layerId)) {
        next.add(layerId);
      }

      return next;
    });
  }

  protected toggle(layerId: string): void {
    this.store.toggle(layerId);
  }

  protected toggleEpicentres(): void {
    this.epicentresVisible.update((visible) => !visible);
  }

  protected close(): void {
    this.closed.emit();
  }
}
