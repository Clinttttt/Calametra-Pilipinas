import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  model,
  output,
  signal,
} from '@angular/core';

import { HazardLayerStore, type LayerState } from '../../../core/layers/hazard-layer-store';
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

  /**
   * Whether the bulk municipality outlines are drawn.
   *
   * Alongside the epicentre control and for the same reason: this is the platform's own rendering
   * decision rather than a catalogued hazard layer, and the reader already comes here to decide what is
   * drawn. Hiding it removes the mesh and keeps whatever municipality is selected, because ADR-005 D6
   * treats a selected unit as part of the answer rather than part of the basemap.
   */
  readonly boundariesVisible = model(true);

  /** Whether the boundary set's provenance and coverage detail is expanded. */
  protected readonly boundaryDetailOpen = signal(false);

  /**
   * What the boundary layer is, in the words a reader needs before interpreting an outline.
   *
   * Held here rather than fetched so the caveat is present the first time the panel opens. The figures
   * are the measured ones and are not rounded up: coverage is not complete, and the two things a reader
   * could most easily get wrong are that these are maritime jurisdiction (they are not) and that every
   * municipality has one (31 do not).
   */
  protected readonly boundarySemantics = {
    summary: 'Land administrative outlines — not municipal waters.',
    attribution: 'OCHA COD-AB, from NAMRIA and the Philippine Statistics Authority',
    licence: 'CC BY 3.0 IGO',
    combinedCoverage: '1,611 of 1,642 cities and municipalities (98.11%)',
    municipalityCoverage: '1,462 of 1,493 municipalities (97.92%)',
    exceptions:
      '31 units have no outline: 23 Maguindanao municipalities the PSA renumbered without publishing '
      + 'a correspondence, and the 8 Bangsamoro Special Geographic Area municipalities, which postdate '
      + 'this edition. They are known exceptions rather than gaps to be guessed at.',
    waters:
      'Philippine cities and municipalities administer waters to 15 km offshore under RA 8550. That '
      + 'extent is a separate concept and is not stored here, so no area or count on this layer covers '
      + 'it. Offshore and proximity questions use the radius instead.',
  } as const;

  /**
   * The map's current zoom.
   *
   * Needed because MapLibre honours a layer's minimum zoom silently: the tick stays on and nothing
   * appears. Without this the panel would present that as a layer that does not work.
   */
  readonly zoom = input(0);

  /** Emitted when the reader asks to zoom in far enough for a layer to draw. */
  readonly zoomRequested = output<number>();

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

  /**
   * Whether a layer is switched on but below the zoom its publisher's service can serve.
   *
   * Reported rather than prevented: the reader's intent is recorded, the layer appears as soon as they
   * are close enough, and the reason is stated in place. Blocking the tick would be worse — it would
   * present a publisher's rendering cost as a broken control.
   */
  protected isBelowMinimumZoom(entry: LayerState): boolean {
    return (
      entry.visible && entry.layer.minimumZoom !== null && this.zoom() < entry.layer.minimumZoom
    );
  }

  protected requestZoom(entry: LayerState): void {
    if (entry.layer.minimumZoom !== null) {
      this.zoomRequested.emit(entry.layer.minimumZoom);
    }
  }

  protected toggleEpicentres(): void {
    this.epicentresVisible.update((visible) => !visible);
  }

  protected toggleBoundaries(): void {
    this.boundariesVisible.update((visible) => !visible);
  }

  protected toggleBoundaryDetail(): void {
    this.boundaryDetailOpen.update((open) => !open);
  }

  protected close(): void {
    this.closed.emit();
  }
}
