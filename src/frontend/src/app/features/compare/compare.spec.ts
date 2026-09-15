import { ChangeDetectionStrategy, Component, input, provideZonelessChangeDetection } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { of } from 'rxjs';

import { CalametraApi } from '../../core/api/calametra-api';
import { Compare } from './compare';
import { LocatorMap } from './locator-map';
import { PageBackdrop } from '../../shared/ui/page-backdrop/page-backdrop';
import { SubjectPicker } from './subject-picker';
import type {
  EarthquakeSummary,
  HazardFeatureCollection,
  HazardLayer,
  NearbyCycloneTracks,
  PaginatedList,
  PlaceContext,
  PlaceMatch,
} from '../../core/api/contracts';

/**
 * Compare page rendering.
 *
 * What is under test is the page's <b>argument</b>, not its arithmetic — the arithmetic is covered in
 * `compare-observations.spec.ts`. Specifically: that the comparable series is the one given headline
 * treatment, that the raw catalogue total is marked as context rather than as a comparison, and that
 * the caveats are reachable. Those are the three properties that distinguish this page from the
 * mirrored-bar scoreboard it replaced, and all three are properties of the template.
 *
 * The locator map is stubbed. MapLibre needs a WebGL context that jsdom does not provide, and the
 * figure it draws is verified against the live API rather than here; what matters for this test is
 * that the card renders and the page reaches its figures.
 */
@Component({
  selector: 'cal-locator-map',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<span data-testid="locator-stub"></span>',
})
class LocatorMapStub {
  readonly latitude = input(0);
  readonly longitude = input(0);
  readonly radiusKm = input(0);
  readonly events = input<unknown>([]);
  readonly faults = input<unknown>(null);
  readonly tracks = input<unknown>([]);
}

/** Stubbed for the same reason: it is a second MapLibre map, and it is a ground rather than a claim. */
@Component({
  selector: 'cal-page-backdrop',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '',
})
class PageBackdropStub {
  readonly zoomOffset = input(0);
}

function match(name: string, psgcCode: string): PlaceMatch {
  return {
    psgcCode,
    name,
    kind: 'City',
    containedBy: 'Province of Surigao del Norte',
    region: 'Caraga',
    latitude: 9.7838,
    longitude: 125.4894,
  };
}

function context(overrides: Partial<PlaceContext>): PlaceContext {
  return {
    psgcCode: '166720000',
    name: 'Surigao City',
    kind: 'City',
    containedBy: 'Province of Surigao del Norte',
    region: 'Caraga',
    latitude: 9.7838,
    longitude: 125.4894,
    radiusKm: 100,
    eventsWithinRadius: 1778,
    eventsAtComparableMagnitude: 40,
    eventsWithAssignedDepth: 889,
    earliestEvent: '1919-03-01T00:00:00Z',
    latestEvent: '2025-10-16T00:00:00Z',
    strongestByScaleFamily: [
      {
        scaleFamily: 'Moment',
        readingsInFamily: 61,
        event: {
          eventId: 'us20008ixa',
          occurredAt: '2017-02-10T14:03:18Z',
          latitude: 9.93,
          longitude: 125.45,
          magnitudeDisplay: 'Mww 6.5',
          depthDisplay: '15 km',
          agency: 'USGS',
          distanceKm: 16.4,
        },
      },
    ],
    mostRecentEvent: {
      eventId: 'us7000abcd',
      occurredAt: '2025-10-16T02:11:00Z',
      latitude: 9.79,
      longitude: 126.11,
      magnitudeDisplay: 'Mww 6.0',
      depthDisplay: '32 km',
      agency: 'USGS',
      distanceKm: 68.9,
    },
    nearestFaults: [
      {
        name: 'Offshore Surigao',
        classification: 'Sinistral',
        distanceKm: 10.9,
        withinRadius: true,
        agency: 'GEM',
        attribution: 'GEM Global Active Faults',
      },
    ],
    cyclones: {
      stormCount: 103,
      landfallCount: 90,
      strongestKnots: 150,
      strongestPeriod: 'OneMinute',
      strongestAgency: 'JTWC',
      firstSeason: 1945,
      lastSeason: 2024,
    },
    notes: ['Counts below M4.0 are not comparable between places.'],
    ...overrides,
  };
}

const EMPTY_PAGE: PaginatedList<EarthquakeSummary> = {
  items: [],
  totalCount: 0,
  page: 1,
  pageSize: 500,
  totalPages: 0,
  hasPreviousPage: false,
  hasNextPage: false,
};

const NO_FAULTS: HazardFeatureCollection = {
  type: 'FeatureCollection',
  features: [],
  attribution: 'GEM Global Active Faults',
  termsUrl: null,
};

const NO_TRACKS: NearbyCycloneTracks = {
  tracks: [],
  stormCount: 0,
  fixCount: 0,
  radiusKm: 100,
  drawnRadiusKm: 160,
  reliableFromSeason: 1985,
};

/** Returns the second place on the second call, so the two sides differ. */
function apiStub(): Partial<CalametraApi> {
  let contexts = 0;

  return {
    searchPlaces: () => of([match('Surigao City', '166720000')]),
    getPlaceContext: () =>
      of(
        contexts++ === 0
          ? context({})
          : context({
              psgcCode: '137404000',
              name: 'Eastern Manila District',
              eventsWithinRadius: 673,
              eventsAtComparableMagnitude: 11,
              eventsWithAssignedDepth: 187,
              cyclones: {
                stormCount: 131,
                landfallCount: 124,
                strongestKnots: 140,
                strongestPeriod: 'OneMinute',
                strongestAgency: 'JTWC',
                firstSeason: 1948,
                lastSeason: 2024,
              },
            }),
      ),
    searchEarthquakes: () => of(EMPTY_PAGE),
    getCycloneTracksNearby: () => of(NO_TRACKS),
    listHazardLayers: () => of([] as readonly HazardLayer[]),
    getHazardFeatures: () => of(NO_FAULTS),
  };
}

interface Rendered {
  readonly fixture: ComponentFixture<Compare>;
  readonly host: HTMLElement;
}

async function renderWithBothPlaces(): Promise<Rendered> {
  const fixture = TestBed.createComponent(Compare);
  await fixture.whenStable();

  const host = fixture.nativeElement as HTMLElement;

  for (const id of ['place-left', 'place-right']) {
    const field = host.querySelector<HTMLInputElement>(`#${id}`);

    field!.value = 'Surigao';
    field!.dispatchEvent(new Event('input'));
    await fixture.whenStable();

    // The match list belongs to the picker that owns the field, so the click has to be scoped to it.
    const picker = field!.closest('cal-subject-picker');

    picker!.querySelector<HTMLButtonElement>('.matches__item')!.click();
    await fixture.whenStable();
  }

  return { fixture, host };
}

describe('Compare page', () => {
  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: CalametraApi, useValue: apiStub() }],
    });

    TestBed.overrideComponent(SubjectPicker, {
      remove: { imports: [LocatorMap] },
      add: { imports: [LocatorMapStub] },
    });

    TestBed.overrideComponent(Compare, {
      remove: { imports: [PageBackdrop] },
      add: { imports: [PageBackdropStub] },
    });

    await TestBed.compileComponents();
  });

  it('opens on the place comparison with both sides empty', async () => {
    const fixture = TestBed.createComponent(Compare);
    await fixture.whenStable();

    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelectorAll('cal-subject-picker').length).toBe(2);
    expect(host.textContent).toContain('Both sides are read at the same radius');
    // Nothing may be stated before both subjects are known.
    expect(host.querySelector('.band')).toBeNull();
  });

  it('identifies each subject before stating any figure', async () => {
    const { host } = await renderWithBothPlaces();
    const cards = host.querySelectorAll('cal-subject-picker .card');

    expect(cards.length).toBe(2);
    expect(cards[0].textContent).toContain('Surigao City');
    expect(cards[0].textContent).toContain('Province of Surigao del Norte');
    expect(cards[0].textContent).toContain('9.7838°N');
    expect(cards[0].querySelector('[data-testid="locator-stub"]')).not.toBeNull();
  });

  it('leads with the comparable series and marks it comparable', async () => {
    const { host } = await renderWithBothPlaces();
    const lead = host.querySelectorAll('.band .metric--lead');

    expect(lead.length).toBe(3);
    expect(lead[0].textContent).toContain('M6.0+ earthquakes');
    expect(lead[0].textContent).toContain('40');
    expect(lead[0].textContent).toContain('11');

    for (const row of lead) {
      expect(row.querySelector('.standing--comparable')).not.toBeNull();
    }
  });

  it('demotes the raw catalogue total to context rather than comparison', async () => {
    const { host } = await renderWithBothPlaces();

    const total = [...host.querySelectorAll('.panel .metric')].find((row) =>
      row.querySelector('.metric__label')?.textContent?.includes('Readings catalogued'),
    );

    expect(total).toBeDefined();
    expect(total!.querySelector('.standing--context')).not.toBeNull();
    expect(total!.querySelector('.standing--comparable')).toBeNull();
    // A figure that must not be compared gets no proportional bar either.
    expect(total!.querySelector('.metric__track')).toBeNull();
  });

  it('states the strongest reading per scale family rather than one largest event', async () => {
    const { host } = await renderWithBothPlaces();

    expect(host.textContent).toContain('Strongest reading, per scale family');
    expect(host.textContent).toContain('Moment');
    expect(host.textContent).toContain('Mww 6.5');
    expect(host.textContent).not.toContain('Largest earthquake');
  });

  it('shows the resolved-depth share as a segmented bar, not a bare percentage', async () => {
    const { host } = await renderWithBothPlaces();
    const bars = host.querySelectorAll('.c-resolved-bar');

    expect(bars.length).toBe(2);
    expect(host.textContent).toContain('measured');
  });

  it('closes with observations and an explicit refusal to rank', async () => {
    const { host } = await renderWithBothPlaces();
    const items = host.querySelectorAll('.stands__list li');

    expect(items.length).toBeGreaterThanOrEqual(3);
    expect(host.textContent).toContain('Neither place is more hazardous');
    expect(host.querySelector('.stands__limit')).not.toBeNull();
  });

  it('keeps the caveats collapsed but reachable', async () => {
    const { fixture, host } = await renderWithBothPlaces();
    const toggle = host.querySelector<HTMLButtonElement>('.c-disclosure__toggle');

    expect(toggle).not.toBeNull();
    expect(toggle!.getAttribute('aria-expanded')).toBe('false');
    expect(host.querySelector('.c-disclosure__body')).toBeNull();

    toggle!.click();
    await fixture.whenStable();

    expect(toggle!.getAttribute('aria-expanded')).toBe('true');
    expect(host.querySelector('.c-disclosure__body')?.textContent).toContain(
      'is not a fair comparison',
    );
  });

  it('exchanges the two sides', async () => {
    const { fixture, host } = await renderWithBothPlaces();
    const swap = host.querySelector<HTMLButtonElement>('.swap');

    expect(swap!.disabled).toBe(false);

    const before = host.querySelectorAll('cal-subject-picker .card__name');

    expect(before[0].textContent).toContain('Surigao City');
    expect(before[1].textContent).toContain('Eastern Manila District');

    swap!.click();
    await fixture.whenStable();

    // Nothing is re-fetched: the stub would hand back the second context twice if it were.
    const after = host.querySelectorAll('cal-subject-picker .card__name');

    expect(after[0].textContent).toContain('Eastern Manila District');
    expect(after[1].textContent).toContain('Surigao City');
  });
});

/**
 * The storm-track figure.
 *
 * Its own suite because the stub has to return tracks, and what is being checked is the caption: the
 * figure draws one line per storm out to a wider radius than the count uses, and where the API's limit
 * bit it draws fewer lines than there are storms. Both are stated rather than left to be inferred from
 * a picture.
 */
describe('Compare storm tracks', () => {
  function withTracks(tracks: NearbyCycloneTracks): Partial<CalametraApi> {
    return { ...apiStub(), getCycloneTracksNearby: () => of(tracks) };
  }

  async function render(tracks: NearbyCycloneTracks): Promise<HTMLElement> {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: CalametraApi, useValue: withTracks(tracks) },
      ],
    });

    TestBed.overrideComponent(SubjectPicker, {
      remove: { imports: [LocatorMap] },
      add: { imports: [LocatorMapStub] },
    });

    TestBed.overrideComponent(Compare, {
      remove: { imports: [PageBackdrop] },
      add: { imports: [PageBackdropStub] },
    });

    await TestBed.compileComponents();

    const { host } = await renderWithBothPlaces();

    return host;
  }

  const track = {
    eventId: '0198f2c2-0000-7000-8000-000000000001',
    name: 'RAI',
    localName: 'ODETTE',
    season: 2021,
    agency: 'Japan Meteorological Agency',
    averagingPeriod: '10-min',
    peakKnotsNearby: 120,
    closestApproachKm: 60.9,
    landfallNearby: true,
    fixes: [
      { capturedAt: '2021-12-16T06:00:00Z', latitude: 9.4, longitude: 126.4, windKnots: 105, isLandfall: false },
      { capturedAt: '2021-12-16T12:00:00Z', latitude: 9.6, longitude: 125.6, windKnots: 120, isLandfall: true },
    ],
  };

  it('states how many tracks are drawn and what they are cut to', async () => {
    const host = await render({
      tracks: [track],
      stormCount: 1,
      fixCount: 2,
      radiusKm: 100,
      drawnRadiusKm: 160,
      reliableFromSeason: 1985,
    });

    const caption = host.querySelector('cal-subject-picker .caption');

    expect(caption?.textContent).toContain('1 storm passages');
    expect(caption?.textContent).toContain('each cut to its approach');
    expect(caption?.textContent).toContain('carry the wind ramp');
  });

  it('discloses the truncation when fewer tracks are drawn than there are storms', async () => {
    const host = await render({
      tracks: [track],
      stormCount: 131,
      fixCount: 2,
      radiusKm: 100,
      drawnRadiusKm: 160,
      reliableFromSeason: 1985,
    });

    expect(host.querySelector('cal-subject-picker .caption')?.textContent).toContain(
      '1 of 131 storm passages, strongest first',
    );
  });

  it('keys the track and landfall marks only when tracks are drawn', async () => {
    const withNone = await render({
      tracks: [],
      stormCount: 0,
      fixCount: 0,
      radiusKm: 100,
      drawnRadiusKm: 160,
      reliableFromSeason: 1985,
    });

    expect(withNone.querySelector('.key--track')).toBeNull();
    expect(withNone.querySelector('cal-subject-picker .caption')).toBeNull();
  });
});
