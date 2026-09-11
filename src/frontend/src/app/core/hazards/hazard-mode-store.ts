import { Injectable, computed, signal } from '@angular/core';

import type { IconName } from '../../shared/ui/icon/icon-paths';
import type { HazardLayer } from '../api/contracts';

/**
 * The hazards this release can explore.
 *
 * A union rather than a free string so that every branch handling a hazard is exhaustive and the
 * compiler reports the ones missed when a third hazard is added.
 */
export type HazardMode = 'earthquakes' | 'cyclones';

/** How a hazard presents itself in the chooser. */
export interface HazardOption {
  readonly mode: HazardMode;
  readonly label: string;
  readonly icon: IconName;
  /** What the hazard is, in one line. */
  readonly summary: string;
  /** The extent of the record held, and whose record it is. */
  readonly coverage: string;
  /**
   * The hazard-layer lens this hazard owns.
   *
   * Published overlays are catalogued by lens server-side, so the hazard names its lens rather than
   * the client keeping a second mapping that could disagree with the catalogue. Today every seeded
   * layer is `Seismic`, which is exactly why this matters: the layers panel was offering fault
   * traces and trenches while the reader was looking at cyclones, and two of them were switched on
   * before any hazard had been chosen at all.
   */
  readonly lens: HazardLayer['lens'];
}

/**
 * WHAT THE MAP IS SHOWING
 *
 * ── Why nothing is selected on open ─────────────────────────────────────────
 * The platform used to open with the entire earthquake catalogue plotted — 27,241 events, drawn
 * several symbols deep over the archipelago. Two things were wrong with that. Visually it was an
 * orange mass that conveyed neither location nor depth, and the one thing it did convey, density,
 * is an artefact of the recording network rather than of seismicity. Structurally it presented one
 * hazard as the platform's subject when the platform is multi-hazard: the reader arrived already
 * inside a dataset they had not asked for, and the cyclone archive sat behind an unlabelled icon
 * of equal weight to six others.
 *
 * So the reader chooses a hazard first, and the interface then shows only that hazard's data and
 * only the tools that apply to it. A cross-section belongs to earthquakes and a wind field belongs
 * to cyclones; presenting both at all times invited the reader to look for a depth profile of a
 * typhoon.
 *
 * ── Why this is a store rather than a signal in the map component ───────────
 * The choice governs which data is fetched, which layers are visible, which tools are offered and
 * which panels may open. Several components need to read it and the map component is not their
 * parent, so it is held centrally and injected rather than threaded through inputs.
 */
@Injectable({ providedIn: 'root' })
export class HazardModeStore {
  /**
   * The hazards offered, in the order presented.
   *
   * Coverage is stated as source and period rather than as a record count. A count would have to be
   * duplicated here and would then drift from the archive the moment another season is ingested;
   * the live figure is shown in the map readout once a hazard is active, where it comes from the
   * data itself.
   */
  static readonly options: readonly HazardOption[] = [
    {
      mode: 'earthquakes',
      label: 'Earthquakes',
      icon: 'lens-seismic',
      summary: 'Epicentres, depth structure and the faults they occur on.',
      coverage: '1901\u20132026 \u00b7 USGS ComCat',
      lens: 'Seismic',
    },
    {
      mode: 'cyclones',
      label: 'Tropical cyclones',
      icon: 'lens-cyclone',
      summary: 'Best tracks and measured wind fields, as each agency drew them.',
      coverage: '2010\u20132026 \u00b7 NOAA IBTrACS',
      lens: 'Cyclone',
    },
  ];

  private readonly _selected = signal<HazardMode | null>(null);

  /** The hazard being explored, or null before the reader has chosen one. */
  readonly selected = this._selected.asReadonly();

  readonly isEarthquakes = computed(() => this._selected() === 'earthquakes');

  readonly isCyclones = computed(() => this._selected() === 'cyclones');

  /** True while the chooser should be shown — that is, before anything has been picked. */
  readonly choosing = computed(() => this._selected() === null);

  /** The active hazard's presentation, for the switcher label. */
  readonly active = computed(() => {
    const mode = this._selected();

    return mode === null
      ? null
      : (HazardModeStore.options.find((option) => option.mode === mode) ?? null);
  });

  select(mode: HazardMode): void {
    this._selected.set(mode);
  }

  /** Returns to the chooser. */
  clear(): void {
    this._selected.set(null);
  }
}
