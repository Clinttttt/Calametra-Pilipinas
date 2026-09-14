import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe, PercentPipe, SlicePipe } from '@angular/common';
import { firstValueFrom } from 'rxjs';

import { CalametraApi } from '../../core/api/calametra-api';
import { Icon } from '../../shared/ui/icon/icon';
import { SubjectPicker } from './subject-picker';
import {
  barShare,
  eraObservations,
  measuredShare,
  placeObservations,
} from './compare-observations';
import type { LocatorEvent } from './locator-map';
import type {
  CatalogueCompleteness,
  CycloneDecade,
  CycloneDecades,
  DecadeSummary,
  HazardFeatureCollection,
  PlaceContext,
  PlaceMatch,
  PlaceStrongestReading,
} from '../../core/api/contracts';

/** Radii offered for a place comparison. Wider than the place explorer's, because comparison is coarser. */
const RADII = [50, 100, 200] as const;

/**
 * The magnitude floor at which counts may be compared between places and between eras.
 *
 * Not a display preference. Detection below this level is a record of how many seismometers were
 * watching, which differs by two orders of magnitude across the archive's century.
 */
const COMPARABLE_MAGNITUDE = 6;

/** Which side of the comparison a control belongs to. */
type Side = 'left' | 'right';

/**
 * How much weight a figure may carry.
 *
 * The distinction is the spine of this page. A `comparable` figure may be read between the two
 * subjects; a `context` figure describes the archive's coverage and must not be; a `quality` figure
 * describes how the values were obtained.
 */
type Standing = 'comparable' | 'context' | 'quality';

/** One figure, stated for both subjects. */
interface ProfileRow {
  readonly label: string;
  readonly standing: Standing;
  readonly left: number | null;
  readonly right: number | null;
  /** Suffix printed after each value, e.g. `per year`. Null for a bare count. */
  readonly unit: string | null;
  /** Shown beneath the label. One clause, not a paragraph. */
  readonly note: string | null;
  /** False where a proportional bar would imply a scale the two values do not share. */
  readonly plotted: boolean;
}

/** The strongest reading in one scale family, for both subjects, aligned so the rows line up. */
interface FamilyRow {
  readonly family: string;
  readonly left: PlaceStrongestReading | null;
  readonly right: PlaceStrongestReading | null;
}

/** Scale families in the order a seismologist reads them: moment first, then the saturating scales. */
const FAMILY_ORDER = ['Moment', 'SurfaceWave', 'BodyWave', 'Local', 'Unknown'] as const;

const FAMILY_LABELS: Readonly<Record<string, string>> = {
  Moment: 'Moment',
  SurfaceWave: 'Surface-wave',
  BodyWave: 'Body-wave',
  Local: 'Local',
  Unknown: 'Unstated',
};

/**
 * A COMPARATIVE HAZARD PROFILE
 *
 * <b>Why this is not a dashboard.</b> The first version of this page was a mirrored bar chart: label
 * down the centre, values outboard, every figure given the same bar. It was accurate and it was
 * wrong, for two reasons. Mirrored bars read as a scoreboard — a winner and a loser — which is
 * exactly the reduction this platform refuses. And giving a catalogue total the same visual weight as
 * an M6.0+ count invited the reader to compare the one figure that cannot honestly be compared.
 *
 * So the page is built as two <em>evidence profiles</em> instead. Each subject gets an identity — its
 * hierarchy, its coordinates, and a locator map drawn at the same scale as its counterpart — and each
 * figure is labelled with what it may be used for: <b>comparable</b>, <b>catalogue context</b>, or
 * <b>data quality</b>. The comparable figures lead. The rest is available but ranked below, which is
 * a claim about evidence made in layout rather than in prose.
 *
 * <b>Why two modes.</b> "Is it worse here than there" and "was it worse then than now" are the two
 * comparisons a reader of a hazard archive actually makes, and they need different machinery: places
 * need a radius and a gazetteer, eras need the catalogue's own completeness. Both live here because
 * the qualifications are identical in each, and stating them on two pages would invite them being
 * read as boilerplate.
 *
 * <b>What this page still refuses to do: score.</b> There is no "Cebu is 1.4× more hazardous than
 * Davao", because no such quantity exists in anything this platform holds — the earthquake counts
 * carry a magnitude floor and a detection history, the storm counts are track proximity rather than
 * impact, and the two hazards share no unit. The closing section states what stands out in the two
 * records and says plainly that it is not a ranking.
 */
@Component({
  selector: 'cal-compare',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, SubjectPicker, DecimalPipe, PercentPipe, SlicePipe],
  host: { class: 'c-page-wash' },
  templateUrl: './compare.html',
  styleUrl: './compare.scss',
})
export class Compare {
  private readonly api = inject(CalametraApi);

  protected readonly radii = RADII;
  protected readonly comparableMagnitude = COMPARABLE_MAGNITUDE;
  protected readonly familyLabels = FAMILY_LABELS;

  protected readonly mode = signal<'places' | 'eras'>('places');
  protected readonly radiusKm = signal<number>(100);

  /** Whether the reading notes are open. Collapsed by default: a caveat nobody reads is not a caveat. */
  protected readonly notesOpen = signal(false);

  // ---- Places -------------------------------------------------------------

  protected readonly leftTerm = signal('');
  protected readonly rightTerm = signal('');
  protected readonly leftMatches = signal<readonly PlaceMatch[]>([]);
  protected readonly rightMatches = signal<readonly PlaceMatch[]>([]);
  protected readonly leftPlace = signal<PlaceContext | null>(null);
  protected readonly rightPlace = signal<PlaceContext | null>(null);
  protected readonly leftLoading = signal(false);
  protected readonly rightLoading = signal(false);

  /** The M6.0+ events plotted on each locator map — the same series the headline count reports. */
  protected readonly leftEvents = signal<readonly LocatorEvent[]>([]);
  protected readonly rightEvents = signal<readonly LocatorEvent[]>([]);

  /** GEM traces, fetched once and handed to both maps. */
  protected readonly faults = signal<HazardFeatureCollection | null>(null);

  // ---- Eras ---------------------------------------------------------------

  protected readonly completeness = signal<CatalogueCompleteness | null>(null);
  protected readonly stormDecades = signal<CycloneDecades | null>(null);
  protected readonly leftDecade = signal(1990);
  protected readonly rightDecade = signal(2020);
  protected readonly erasLoading = signal(false);

  protected readonly bothPlaces = computed(() => {
    const left = this.leftPlace();
    const right = this.rightPlace();

    return left !== null && right !== null ? { left, right } : null;
  });

  /** Decades the catalogue actually holds, so a comparison cannot be offered for an empty one. */
  protected readonly decades = computed(
    () => this.completeness()?.decades.map((decade) => decade.decade) ?? [],
  );

  protected readonly bothEras = computed(() => {
    const quakes = this.completeness()?.decades ?? [];
    const storms = this.stormDecades()?.decades ?? [];

    const left = quakes.find((decade) => decade.decade === this.leftDecade());
    const right = quakes.find((decade) => decade.decade === this.rightDecade());

    if (left === undefined || right === undefined) {
      return null;
    }

    return {
      left,
      right,
      leftStorms: storms.find((decade) => decade.decade === this.leftDecade()) ?? null,
      rightStorms: storms.find((decade) => decade.decade === this.rightDecade()) ?? null,
    };
  });

  // ---- The signals a reader may actually compare ---------------------------

  /**
   * The headline band: only figures that survive the archive's own caveats.
   *
   * Three rows, and no more. A "what can be compared" section that listed eight things would be a
   * table again.
   */
  protected readonly comparableSignals = computed<readonly ProfileRow[]>(() => {
    if (this.mode() === 'places') {
      const pair = this.bothPlaces();

      if (pair === null) {
        return [];
      }

      return [
        {
          label: `M${COMPARABLE_MAGNITUDE.toFixed(1)}+ earthquakes`,
          standing: 'comparable',
          left: pair.left.eventsAtComparableMagnitude,
          right: pair.right.eventsAtComparableMagnitude,
          unit: null,
          note: 'Above the floor where detection is even across the archive',
          plotted: true,
        },
        {
          label: 'Storms passing within range',
          standing: 'comparable',
          left: pair.left.cyclones.stormCount,
          right: pair.right.cyclones.stormCount,
          unit: null,
          note: 'Best-track positions inside the radius — proximity, not impact',
          plotted: true,
        },
        {
          label: 'Of those, crossing land nearby',
          standing: 'comparable',
          left: pair.left.cyclones.landfallCount,
          right: pair.right.cyclones.landfallCount,
          unit: null,
          note: 'The series least dependent on what could be observed at sea',
          plotted: true,
        },
      ];
    }

    const eras = this.bothEras();

    if (eras === null) {
      return [];
    }

    return [
      {
        label: `M${COMPARABLE_MAGNITUDE.toFixed(1)}+ earthquakes per year`,
        standing: 'comparable',
        left: eras.left.comparablePerYear,
        right: eras.right.comparablePerYear,
        unit: 'per year',
        note: 'Flat at roughly five a year across a century — the rate that is genuinely stable',
        plotted: true,
      },
      {
        label: 'Storms in the archive',
        standing: 'comparable',
        left: eras.leftStorms?.stormCount ?? null,
        right: eras.rightStorms?.stormCount ?? null,
        unit: null,
        note: 'Basin-wide count for the decade',
        plotted: true,
      },
      {
        label: 'Of those, making landfall',
        standing: 'comparable',
        left: eras.leftStorms?.landfallCount ?? null,
        right: eras.rightStorms?.landfallCount ?? null,
        unit: null,
        note: 'Landfall was recorded long before satellites resolved storms at sea',
        plotted: true,
      },
    ];
  });

  /** The earthquake profile below the headline: coverage and quality, plainly demoted. */
  protected readonly earthquakeRows = computed<readonly ProfileRow[]>(() => {
    if (this.mode() === 'places') {
      const pair = this.bothPlaces();

      if (pair === null) {
        return [];
      }

      return [
        {
          label: 'Readings catalogued',
          standing: 'context',
          left: pair.left.eventsWithinRadius,
          right: pair.right.eventsWithinRadius,
          unit: null,
          note: 'A record of observation as much as of seismicity',
          plotted: false,
        },
        {
          label: `M${COMPARABLE_MAGNITUDE.toFixed(1)} and above`,
          standing: 'comparable',
          left: pair.left.eventsAtComparableMagnitude,
          right: pair.right.eventsAtComparableMagnitude,
          unit: null,
          note: null,
          plotted: true,
        },
        {
          label: 'Depth assigned, not measured',
          standing: 'quality',
          left: pair.left.eventsWithAssignedDepth,
          right: pair.right.eventsWithAssignedDepth,
          unit: null,
          note: 'A convention such as 33 km, used when depth could not be resolved',
          plotted: false,
        },
      ];
    }

    const eras = this.bothEras();

    if (eras === null) {
      return [];
    }

    return [
      {
        label: 'Readings catalogued',
        standing: 'context',
        left: eras.left.totalCount,
        right: eras.right.totalCount,
        unit: null,
        note: 'Rises 285-fold across the century. That is instrumentation, not seismicity',
        plotted: false,
      },
      {
        label: `M${COMPARABLE_MAGNITUDE.toFixed(1)}+ per year`,
        standing: 'comparable',
        left: eras.left.comparablePerYear,
        right: eras.right.comparablePerYear,
        unit: 'per year',
        note: null,
        plotted: true,
      },
    ];
  });

  /** The proportion of depths that were actually resolved, for the segmented quality bar. */
  protected readonly depthQuality = computed(() => {
    if (this.mode() === 'places') {
      const pair = this.bothPlaces();

      if (pair === null) {
        return null;
      }

      return {
        left: measuredShare(pair.left.eventsWithinRadius, pair.left.eventsWithAssignedDepth),
        right: measuredShare(pair.right.eventsWithinRadius, pair.right.eventsWithAssignedDepth),
      };
    }

    const eras = this.bothEras();

    if (eras === null) {
      return null;
    }

    return {
      left: 1 - eras.left.assignedDepthShare,
      right: 1 - eras.right.assignedDepthShare,
    };
  });

  /**
   * Strongest reading per scale family, aligned across both subjects.
   *
   * Per family rather than as one "largest nearby earthquake", because `mb` saturates near M6 and
   * cannot be ranked against a moment magnitude. Where one side has a family the other lacks, the
   * row still appears with an em dash — an absent family is itself a fact about the record.
   */
  protected readonly familyRows = computed<readonly FamilyRow[]>(() => {
    const pair = this.bothPlaces();

    if (pair === null) {
      return [];
    }

    const present = new Set([
      ...pair.left.strongestByScaleFamily.map((reading) => reading.scaleFamily),
      ...pair.right.strongestByScaleFamily.map((reading) => reading.scaleFamily),
    ]);

    const ordered = [
      ...FAMILY_ORDER.filter((family) => present.has(family)),
      ...[...present].filter((family) => !FAMILY_ORDER.includes(family as never)),
    ];

    return ordered.map((family) => ({
      family,
      left: pair.left.strongestByScaleFamily.find((r) => r.scaleFamily === family) ?? null,
      right: pair.right.strongestByScaleFamily.find((r) => r.scaleFamily === family) ?? null,
    }));
  });

  /**
   * Whether either chosen decade predates the storm record's dependable intensities.
   *
   * Surfaced rather than assumed: JTWC rates its own western North Pacific best track as high
   * quality only from 1985, so a wind comparison across that boundary compares a measurement with
   * an estimate.
   */
  protected readonly stormIntensityCaveat = computed(() => {
    const from = this.stormDecades()?.reliableFromSeason;

    if (from === undefined) {
      return false;
    }

    return this.leftDecade() + 9 < from || this.rightDecade() + 9 < from;
  });

  /**
   * Two or three neutral observations, composed from the figures on screen.
   *
   * The wording lives in `compare-observations.ts` and is unit-tested there: these are the only
   * sentences on the page that state a conclusion, and a sentence that overstates what the archive
   * supports is an invisible defect in a way a wrong number is not.
   */
  protected readonly standsOut = computed<readonly string[]>(() => {
    if (this.mode() === 'places') {
      const pair = this.bothPlaces();

      return pair === null
        ? []
        : placeObservations(pair.left, pair.right, this.radiusKm(), COMPARABLE_MAGNITUDE);
    }

    const eras = this.bothEras();

    return eras === null
      ? []
      : eraObservations(
          eras.left,
          eras.right,
          eras.leftStorms,
          eras.rightStorms,
          COMPARABLE_MAGNITUDE,
        );
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

    (side === 'left' ? this.leftMatches : this.rightMatches).set([]);
    (side === 'left' ? this.leftTerm : this.rightTerm).set(place.name);

    void this.loadFaults();
    await this.readPlace(side, place.psgcCode, this.radiusKm());
  }

  /** Exchanges the two sides. Nothing is re-fetched: both contexts are already in hand. */
  protected swap(): void {
    const [leftPlace, rightPlace] = [this.leftPlace(), this.rightPlace()];
    const [leftTerm, rightTerm] = [this.leftTerm(), this.rightTerm()];
    const [leftEvents, rightEvents] = [this.leftEvents(), this.rightEvents()];

    this.leftPlace.set(rightPlace);
    this.rightPlace.set(leftPlace);
    this.leftTerm.set(rightTerm);
    this.rightTerm.set(leftTerm);
    this.leftEvents.set(rightEvents);
    this.rightEvents.set(leftEvents);

    if (this.mode() === 'eras') {
      const [left, right] = [this.leftDecade(), this.rightDecade()];

      this.leftDecade.set(right);
      this.rightDecade.set(left);
    }
  }

  /** Re-reads both sides at a new radius, since a comparison at mixed radii would be meaningless. */
  protected async setRadius(km: number): Promise<void> {
    if (km === this.radiusKm()) {
      return;
    }

    this.radiusKm.set(km);

    const sides: readonly Side[] = ['left', 'right'];

    await Promise.all(
      sides.map((side) => {
        const code = (side === 'left' ? this.leftPlace() : this.rightPlace())?.psgcCode;

        return code ? this.readPlace(side, code, km) : Promise.resolve();
      }),
    );
  }

  /**
   * The share of a bar, for a paired proportional bar. See `barShare`.
   *
   * Kept as a method because a template cannot call a bare import.
   */
  protected share(value: number | null, other: number | null): number {
    return barShare(value, other);
  }

  protected label(decade: number): string {
    return `${decade}s`;
  }

  protected decadeStorms(decade: DecadeSummary): CycloneDecade | null {
    return this.stormDecades()?.decades.find((entry) => entry.decade === decade.decade) ?? null;
  }

  // ---- Loading ------------------------------------------------------------

  private async readPlace(side: Side, psgcCode: string, km: number): Promise<void> {
    const loading = side === 'left' ? this.leftLoading : this.rightLoading;
    const context = side === 'left' ? this.leftPlace : this.rightPlace;
    const events = side === 'left' ? this.leftEvents : this.rightEvents;

    loading.set(true);

    try {
      const read = await firstValueFrom(this.api.getPlaceContext(psgcCode, km));

      context.set(read);
      events.set(await this.readComparableEvents(read));
    } finally {
      loading.set(false);
    }
  }

  /**
   * The M6.0+ events inside the radius, for the locator map.
   *
   * A second request rather than an addition to the place context, because the context is a summary
   * and this is geometry — and because the same endpoint, with the same parameters, is what produced
   * the count. The map therefore cannot disagree with the number beside it.
   */
  private async readComparableEvents(context: PlaceContext): Promise<readonly LocatorEvent[]> {
    const page = await firstValueFrom(
      this.api.searchEarthquakes({
        centreLatitude: context.latitude,
        centreLongitude: context.longitude,
        radiusKm: context.radiusKm,
        minMagnitude: COMPARABLE_MAGNITUDE,
        pageSize: 500,
      }),
    );

    return page.items.map((event) => ({
      latitude: event.latitude,
      longitude: event.longitude,
      magnitude: event.magnitude?.value ?? null,
      depthKm: event.depth.kilometres,
      depthMeasured: event.depth.isMeasured,
    }));
  }

  /**
   * Fetches the stored fault traces once.
   *
   * Discovered from the layer catalogue rather than hard-coded: which layers may be stored is a
   * licensing fact the server owns, and `LocalVector` is how it says so.
   */
  private async loadFaults(): Promise<void> {
    if (this.faults() !== null) {
      return;
    }

    const layers = await firstValueFrom(this.api.listHazardLayers('Seismic'));
    const stored = layers.find((layer) => layer.deliveryMode === 'LocalVector');

    if (stored === undefined) {
      return;
    }

    this.faults.set(await firstValueFrom(this.api.getHazardFeatures(stored.id)));
  }
}
