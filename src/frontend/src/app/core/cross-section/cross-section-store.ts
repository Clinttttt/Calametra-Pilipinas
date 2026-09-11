import { computed, inject, signal } from '@angular/core';
import { Injectable } from '@angular/core';

import { CalametraApi } from '../api/calametra-api';
import type { CrossSection } from '../api/contracts';
import { NotificationStore } from '../notifications/notification-store';
import { SECTION_PRESETS, type SectionPreset } from './section-presets';

/** Where the user is in the draw-a-section interaction. */
export type CrossSectionPhase =
  /** Not sectioning. The tool is closed. */
  | 'idle'
  /** Waiting for the first map click. */
  | 'awaiting-start'
  /** First endpoint placed, waiting for the second. */
  | 'awaiting-end'
  /** Both endpoints placed; a profile is loaded or loading. */
  | 'ready';

export interface SectionEndpoint {
  readonly latitude: number;
  readonly longitude: number;
}

/**
 * Owns the intent behind the cross-section tool: where the line is, how wide the
 * corridor is, and whether unmeasured depths are shown.
 *
 * The map component owns the consequence — drawing the line and the corridor — and the
 * plot component owns rendering the profile. Neither reaches into the other.
 */
@Injectable({ providedIn: 'root' })
export class CrossSectionStore {
  private readonly api = inject(CalametraApi);
  private readonly notifications = inject(NotificationStore);

  private readonly _phase = signal<CrossSectionPhase>('idle');
  private readonly _start = signal<SectionEndpoint | null>(null);
  private readonly _end = signal<SectionEndpoint | null>(null);
  private readonly _corridorKm = signal(50);
  private readonly _includeAssignedDepths = signal(false);
  private readonly _minMagnitude = signal<number | null>(null);
  private readonly _profile = signal<CrossSection | null>(null);
  private readonly _loading = signal(false);
  private readonly _hoveredEventId = signal<string | null>(null);

  /**
   * Which preset is currently drawn, if any.
   *
   * Cleared as soon as the user places their own points or changes the corridor, because
   * from that moment the plot no longer matches the preset's verified figures and
   * labelling it with the preset's name would misattribute it.
   */
  private readonly _activePresetId = signal<string | null>(null);

  readonly phase = this._phase.asReadonly();
  readonly start = this._start.asReadonly();
  readonly end = this._end.asReadonly();
  readonly corridorKm = this._corridorKm.asReadonly();
  readonly includeAssignedDepths = this._includeAssignedDepths.asReadonly();
  readonly minMagnitude = this._minMagnitude.asReadonly();
  readonly profile = this._profile.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly hoveredEventId = this._hoveredEventId.asReadonly();
  readonly activePresetId = this._activePresetId.asReadonly();

  /** The preset currently drawn, resolved to its definition. */
  readonly activePreset = computed(
    () => SECTION_PRESETS.find((preset) => preset.id === this._activePresetId()) ?? null,
  );

  /** Offered when nothing is drawn yet. */
  readonly presets = SECTION_PRESETS;

  /** True while the map should be capturing clicks as endpoint placement. */
  readonly capturingClicks = computed(
    () => this._phase() === 'awaiting-start' || this._phase() === 'awaiting-end',
  );

  /** True once there is a complete line to draw, whether or not data has arrived. */
  readonly hasLine = computed(() => this._start() !== null && this._end() !== null);

  /** Opens the tool and begins endpoint placement. */
  open(): void {
    if (this._phase() === 'idle') {
      this._phase.set('awaiting-start');
    }
  }

  /** Closes the tool and discards the section entirely. */
  close(): void {
    this._phase.set('idle');
    this._start.set(null);
    this._end.set(null);
    this._profile.set(null);
    this._hoveredEventId.set(null);
    this._activePresetId.set(null);
  }

  /** Clears the line but keeps the tool open for another attempt. */
  reset(): void {
    this._start.set(null);
    this._end.set(null);
    this._profile.set(null);
    this._hoveredEventId.set(null);
    this._activePresetId.set(null);
    this._phase.set('awaiting-start');
  }

  /**
   * Draws one of the verified preset sections.
   *
   * Skips endpoint placement entirely: the endpoints are known good, so the reader gets
   * a correct profile without first having to know where the trenches are or which way
   * they dip.
   */
  applyPreset(preset: SectionPreset): void {
    this._start.set(preset.start);
    this._end.set(preset.end);
    this._corridorKm.set(preset.corridorKm);
    this._activePresetId.set(preset.id);
    this._phase.set('ready');
    this.load();
  }

  /**
   * Records a map click as the next endpoint.
   *
   * Ignored outside the capturing phases so a click meant for a marker cannot silently
   * move a completed section.
   */
  placePoint(latitude: number, longitude: number): void {
    const phase = this._phase();

    if (phase === 'awaiting-start') {
      this._start.set({ latitude, longitude });
      this._end.set(null);
      this._profile.set(null);
      this._activePresetId.set(null);
      this._phase.set('awaiting-end');
      return;
    }

    if (phase === 'awaiting-end') {
      this._end.set({ latitude, longitude });
      this._phase.set('ready');
      this.load();
    }
  }

  setCorridorKm(km: number): void {
    if (km === this._corridorKm()) {
      return;
    }

    this._corridorKm.set(km);

    // The preset's published figures were measured at its own corridor width, so the
    // attribution no longer holds once that changes.
    this._activePresetId.set(null);

    if (this._phase() === 'ready') {
      this.load();
    }
  }

  setIncludeAssignedDepths(include: boolean): void {
    if (include === this._includeAssignedDepths()) {
      return;
    }

    this._includeAssignedDepths.set(include);

    if (this._phase() === 'ready') {
      this.load();
    }
  }

  setMinMagnitude(magnitude: number | null): void {
    this._minMagnitude.set(magnitude);

    if (this._phase() === 'ready') {
      this.load();
    }
  }

  setHoveredEvent(eventId: string | null): void {
    this._hoveredEventId.set(eventId);
  }

  private load(): void {
    const start = this._start();
    const end = this._end();

    if (!start || !end) {
      return;
    }

    this._loading.set(true);

    this.api
      .getCrossSection({
        startLatitude: start.latitude,
        startLongitude: start.longitude,
        endLatitude: end.latitude,
        endLongitude: end.longitude,
        corridorKm: this._corridorKm(),
        includeAssignedDepths: this._includeAssignedDepths(),
        minMagnitude: this._minMagnitude() ?? undefined,
      })
      .subscribe({
        next: (profile) => {
          this._profile.set(profile);
          this._loading.set(false);
        },
        error: () => {
          this._loading.set(false);
          this.notifications.error('The cross-section could not be computed.');
        },
      });
  }
}
