import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe, PercentPipe } from '@angular/common';
import { firstValueFrom } from 'rxjs';

import { CalametraApi } from '../../core/api/calametra-api';
import { Icon } from '../../shared/ui/icon/icon';
import type {
  CatalogueCompleteness,
  CycloneDecades,
  PlaceContext,
  PlaceMatch,
} from '../../core/api/contracts';

/** Radii offered for a place comparison. Wider than the place explorer's, because comparison is coarser. */
const RADII = [50, 100, 200] as const;

/** Which side of the comparison a control belongs to. */
type Side = 'left' | 'right';

/**
 * Two subjects, side by side, with every figure qualified.
 *
 * <b>Why two modes rather than one.</b> "Is it worse here than there" and "was it worse then than now"
 * are the two comparisons a reader of a hazard archive actually makes, and they need different
 * machinery: places need a radius and a gazetteer, eras need the catalogue's own completeness. Both are
 * offered here rather than split across two pages, because the qualifications are the same in each and
 * stating them twice would invite them being read as boilerplate.
 *
 * <b>What this page refuses to do.</b> It does not score, rank or total. There is no "Cebu is 1.4×
 * more hazardous than Davao" figure, because no such quantity exists in anything this platform holds:
 * the earthquake counts carry a magnitude floor and a detection history, the storm counts are track
 * proximity rather than impact, and the two hazards share no unit. The page puts the numbers beside
 * each other and states what each one is; the comparison is the reader's to make.
 */
@Component({
  selector: 'cal-compare',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, DecimalPipe, PercentPipe],
  host: { class: 'c-page-wash' },
  templateUrl: './compare.html',
  styleUrl: './compare.scss',
})
export class Compare {
  private readonly api = inject(CalametraApi);

  protected readonly radii = RADII;

  protected readonly mode = signal<'places' | 'eras'>('places');
  protected readonly radiusKm = signal<number>(100);

  // ---- Places -------------------------------------------------------------

  protected readonly leftTerm = signal('');
  protected readonly rightTerm = signal('');
  protected readonly leftMatches = signal<readonly PlaceMatch[]>([]);
  protected readonly rightMatches = signal<readonly PlaceMatch[]>([]);
  protected readonly leftPlace = signal<PlaceContext | null>(null);
  protected readonly rightPlace = signal<PlaceContext | null>(null);
  protected readonly leftLoading = signal(false);
  protected readonly rightLoading = signal(false);

  // ---- Eras ---------------------------------------------------------------

  protected readonly completeness = signal<CatalogueCompleteness | null>(null);
  protected readonly stormDecades = signal<CycloneDecades | null>(null);
  protected readonly leftDecade = signal(1990);
  protected readonly rightDecade = signal(2020);
  protected readonly erasLoading = signal(false);

  /** Decades present in both series, so a comparison cannot be offered for a decade with no data. */
  protected readonly decades = computed(() => {
    const quakes = this.completeness()?.decades.map((decade) => decade.decade) ?? [];

    return quakes;
  });

  protected readonly leftQuakeDecade = computed(() =>
    this.completeness()?.decades.find((decade) => decade.decade === this.leftDecade()) ?? null,
  );

  protected readonly rightQuakeDecade = computed(() =>
    this.completeness()?.decades.find((decade) => decade.decade === this.rightDecade()) ?? null,
  );

  protected readonly leftStormDecade = computed(() =>
    this.stormDecades()?.decades.find((decade) => decade.decade === this.leftDecade()) ?? null,
  );

  protected readonly rightStormDecade = computed(() =>
    this.stormDecades()?.decades.find((decade) => decade.decade === this.rightDecade()) ?? null,
  );

  /**
   * Whether either chosen decade predates the storm record's dependable intensities.
   *
   * Surfaced rather than assumed: JTWC rates its own western North Pacific best track as high quality
   * only from 1985, so a wind comparison across that boundary is comparing a measurement with an
   * estimate.
   */
  protected readonly stormIntensityCaveat = computed(() => {
    const from = this.stormDecades()?.reliableFromSeason;

    if (from === undefined) {
      return false;
    }

    return this.leftDecade() + 9 < from || this.rightDecade() + 9 < from;
  });

  protected setMode(mode: 'places' | 'eras'): void {
    this.mode.set(mode);

    if (mode === 'eras' && this.completeness() === null) {
      void this.loadEras();
    }
  }

  protected async loadEras(): Promise<void> {
    this.erasLoading.set(true);

    try {
      const [quakes, storms] = await Promise.all([
        firstValueFrom(this.api.getCatalogueCompleteness()),
        firstValueFrom(this.api.getCycloneDecades()),
      ]);

      this.completeness.set(quakes);
      this.stormDecades.set(storms);
    } finally {
      this.erasLoading.set(false);
    }
  }

  protected setDecade(side: Side, decade: number): void {
    (side === 'left' ? this.leftDecade : this.rightDecade).set(decade);
  }

  protected async search(side: Side, term: string): Promise<void> {
    (side === 'left' ? this.leftTerm : this.rightTerm).set(term);

    if (term.trim().length < 2) {
      (side === 'left' ? this.leftMatches : this.rightMatches).set([]);

      return;
    }

    const matches = await firstValueFrom(this.api.searchPlaces(term.trim()));

    (side === 'left' ? this.leftMatches : this.rightMatches).set(matches.slice(0, 6));
  }

  protected async choose(side: Side, place: PlaceMatch): Promise<void> {
    if (place.psgcCode === null) {
      return;
    }

    const loading = side === 'left' ? this.leftLoading : this.rightLoading;
    const target = side === 'left' ? this.leftPlace : this.rightPlace;

    (side === 'left' ? this.leftMatches : this.rightMatches).set([]);
    (side === 'left' ? this.leftTerm : this.rightTerm).set(place.name);
    loading.set(true);

    try {
      target.set(await firstValueFrom(this.api.getPlaceContext(place.psgcCode, this.radiusKm())));
    } finally {
      loading.set(false);
    }
  }

  /** Re-reads both sides at a new radius, since a comparison at mixed radii would be meaningless. */
  protected async setRadius(km: number): Promise<void> {
    if (km === this.radiusKm()) {
      return;
    }

    this.radiusKm.set(km);

    const reload = async (context: PlaceContext | null, side: Side): Promise<void> => {
      if (context?.psgcCode) {
        const loading = side === 'left' ? this.leftLoading : this.rightLoading;
        const target = side === 'left' ? this.leftPlace : this.rightPlace;

        loading.set(true);

        try {
          target.set(await firstValueFrom(this.api.getPlaceContext(context.psgcCode, km)));
        } finally {
          loading.set(false);
        }
      }
    };

    await Promise.all([reload(this.leftPlace(), 'left'), reload(this.rightPlace(), 'right')]);
  }

  /**
   * The share of a bar, for the paired bars beside each figure.
   *
   * Proportional to the larger of the two values rather than to a fixed scale, because the comparison is
   * between these two subjects and nothing else. Returns zero when both are zero, so an empty pair draws
   * nothing rather than two full bars.
   */
  protected share(value: number | null, other: number | null): number {
    const peak = Math.max(value ?? 0, other ?? 0);

    return peak === 0 ? 0 : Math.round(((value ?? 0) / peak) * 100);
  }

  protected label(decade: number): string {
    return `${decade}s`;
  }
}
