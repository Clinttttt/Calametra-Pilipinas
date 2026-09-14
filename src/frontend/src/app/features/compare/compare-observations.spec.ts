import {
  barShare,
  eraObservations,
  measuredShare,
  placeObservations,
} from './compare-observations';
import type { CycloneDecade, DecadeSummary, PlaceContext } from '../../core/api/contracts';

/**
 * These sentences are the only place the Compare page states a conclusion in prose, so what is under
 * test is not only that the numbers are interpolated correctly but that the <em>wording</em> stays
 * within what the archive supports. The vocabulary assertions are deliberate: a future edit that
 * reintroduces "more dangerous" or "worst" should fail the build, because a page whose whole argument
 * is that hazards cannot be ranked must not rank them in its closing paragraph.
 */

/** Words that would turn an observation about a record into a claim about the world. */
const FORBIDDEN = [
  'more dangerous',
  'most dangerous',
  'safer',
  'safest',
  'worst',
  'riskiest',
  'higher risk',
  'ranked',
  'winner',
];

function place(overrides: Partial<PlaceContext> = {}): PlaceContext {
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
    eventsWithAssignedDepth: 900,
    earliestEvent: '1919-03-01T00:00:00Z',
    latestEvent: '2025-10-16T00:00:00Z',
    strongestByScaleFamily: [],
    mostRecentEvent: null,
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
    notes: [],
    ...overrides,
  };
}

function decade(overrides: Partial<DecadeSummary> = {}): DecadeSummary {
  return {
    decade: 1990,
    totalCount: 2000,
    comparableCount: 64,
    comparablePerYear: 6.4,
    assignedDepthCount: 800,
    assignedDepthShare: 0.4,
    dominantScale: 'mb',
    strongestMagnitude: 7.8,
    strongestScale: 'Mww',
    ...overrides,
  };
}

function storms(overrides: Partial<CycloneDecade> = {}): CycloneDecade {
  return {
    decade: 1990,
    stormCount: 300,
    landfallCount: 120,
    namedInPhilippinesCount: 12,
    strongestKnots: 160,
    ...overrides,
  };
}

describe('placeObservations', () => {
  const surigao = place();
  const manila = place({
    psgcCode: '137404000',
    name: 'Eastern Manila District',
    eventsWithinRadius: 673,
    eventsAtComparableMagnitude: 11,
    nearestFaults: [
      {
        name: 'East Valley Fault',
        classification: 'Dextral',
        distanceKm: 8.2,
        withinRadius: true,
        agency: 'GEM',
        attribution: 'GEM Global Active Faults',
      },
    ],
    cyclones: { ...surigao.cyclones, stormCount: 131, landfallCount: 124 },
  });

  it('states the earthquake comparison as a property of the archive, not of the place', () => {
    const [first] = placeObservations(surigao, manila, 100, 6);

    expect(first).toContain("Surigao City's archive holds more");
    expect(first).toContain('40 against 11');
    expect(first).toContain('within 100 km');
  });

  it('names whichever side leads, not whichever side is first', () => {
    const [first] = placeObservations(manila, surigao, 100, 6);

    expect(first).toContain("Surigao City's archive holds more");
  });

  it('says the series does not separate them when the comparable counts are equal', () => {
    const observations = placeObservations(surigao, place({ name: 'Butuan City' }), 100, 6);

    expect(observations[0]).toContain('does not separate them');
    // No lead was found, so no name may be put ahead of the other.
    expect(observations[0]).not.toContain('holds more');
  });

  it('reports the landfall series rather than the raw storm count', () => {
    const observations = placeObservations(surigao, manila, 100, 6);
    const storm = observations.find((line) => line.includes('crossing land'));

    // 124 against 90 landfalls, not 131 against 103 tracks.
    expect(storm).toContain('124 against 90');
    expect(storm).not.toContain('131');
  });

  it('omits the storm observation when neither side leads', () => {
    const observations = placeObservations(surigao, place({ name: 'Butuan City' }), 100, 6);

    expect(observations.some((line) => line.includes('crossing land'))).toBe(false);
  });

  it('states fault proximity for both sides, with distances', () => {
    const observations = placeObservations(surigao, manila, 100, 6);
    const fault = observations.find((line) => line.startsWith('Nearest mapped trace'));

    expect(fault).toContain('Offshore Surigao at 10.9 km');
    expect(fault).toContain('East Valley Fault at 8.2 km');
  });

  it('omits fault proximity when either side has no mapped trace', () => {
    const observations = placeObservations(surigao, place({ nearestFaults: [] }), 100, 6);

    expect(observations.some((line) => line.startsWith('Nearest mapped trace'))).toBe(false);
  });

  it('closes on a limit that names all three caveats', () => {
    const observations = placeObservations(surigao, manila, 100, 6);
    const last = observations[observations.length - 1];

    expect(last).toContain('not occurrence rates');
    expect(last).toContain('track proximity rather than impact');
    expect(last).toContain('an unmapped fault is not an absent one');
  });

  it('never ranks the two places', () => {
    const observations = placeObservations(surigao, manila, 100, 6).join(' ').toLowerCase();

    for (const word of FORBIDDEN) {
      // "more hazardous" appears once, and only inside the negation "Neither place is more
      // hazardous", which is the sentence that forbids the ranking rather than making one.
      expect(observations).not.toContain(word);
    }

    expect(observations).toContain('neither place is more hazardous');
  });

  it('carries the magnitude floor through rather than hard-coding it', () => {
    const [first] = placeObservations(surigao, manila, 100, 7);

    expect(first).toContain('M7.0+');
  });
});

describe('eraObservations', () => {
  const nineties = decade({ decade: 1990, totalCount: 2000, comparablePerYear: 6.4 });
  const twenties = decade({
    decade: 2020,
    totalCount: 5974,
    comparablePerYear: 6.0,
    dominantScale: 'mb',
  });

  it('puts the total and the comparable rate in one sentence, because the gap is the finding', () => {
    const [first] = eraObservations(nineties, twenties, null, null, 6);

    expect(first).toContain('3.0×');
    expect(first).toContain('6.4 to 6 a year');
  });

  it('says "about as many" rather than a ratio when the totals are within a tenth', () => {
    const [first] = eraObservations(nineties, decade({ decade: 2000, totalCount: 2050 }), null, null, 6);

    expect(first).toContain('about as many');
  });

  it('flags a change of dominant scale as a change in the kind of number', () => {
    const observations = eraObservations(
      nineties,
      decade({ decade: 2020, dominantScale: 'mww' }),
      null,
      null,
      6,
    );

    expect(observations[1]).toContain('not the same kind of number');
    expect(observations[1]).toContain('mb in the 1990s against mww in the 2020s');
  });

  it('stays silent about scale when both decades share one', () => {
    const observations = eraObservations(nineties, twenties, null, null, 6);

    expect(observations.some((line) => line.includes('kind of number'))).toBe(false);
  });

  it('reports landfalls against the number tracked, so coverage is visible', () => {
    const observations = eraObservations(
      nineties,
      twenties,
      storms({ decade: 1990, landfallCount: 120, stormCount: 300 }),
      storms({ decade: 2020, landfallCount: 98, stormCount: 240 }),
      6,
    );

    const line = observations.find((entry) => entry.startsWith('Landfalling storms'));

    expect(line).toContain('120 in the 1990s against 98 in the 2020s');
    expect(line).toContain('out of 300 and 240 tracked');
  });

  it('closes by attributing the rise to instrumentation', () => {
    const observations = eraObservations(nineties, twenties, null, null, 6);
    const last = observations[observations.length - 1];

    expect(last).toContain('instrumentation, not seismicity');
    expect(last).toContain('M6.0+');
  });
});

describe('measuredShare', () => {
  it('returns the resolved proportion', () => {
    expect(measuredShare(1000, 430)).toBeCloseTo(0.57, 10);
  });

  it('returns null for an empty population rather than claiming 0% measured', () => {
    expect(measuredShare(0, 0)).toBeNull();
  });
});

describe('barShare', () => {
  it('scales to the larger of the two values', () => {
    expect(barShare(40, 11)).toBe(100);
    expect(barShare(11, 40)).toBe(28);
  });

  it('draws nothing when both values are zero', () => {
    expect(barShare(0, 0)).toBe(0);
  });

  it('treats a missing value as absent rather than as zero-of-zero', () => {
    expect(barShare(null, 50)).toBe(0);
    expect(barShare(50, null)).toBe(100);
  });
});
