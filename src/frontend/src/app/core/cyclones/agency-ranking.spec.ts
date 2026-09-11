import { describe, expect, it } from 'vitest';

import { mostFullyReported } from './agency-ranking';
import type { AgencyTrack, CycloneFix } from '../api/contracts';

/**
 * Which agency's reading the panel opens with.
 *
 * The counts below are the real ones, read from `/api/cyclones/{id}` for SURIGAE 2021. They are
 * reproduced here because the rule was got wrong twice against exactly this storm: first by taking
 * whichever track the API sorted first, which selected an agency publishing no wind extent at all;
 * then by testing only for the *presence* of extent, which tie-broke on fix count and selected the
 * agency publishing a bare ellipse over the one publishing quadrants, inner bands and an eyewall.
 */
function fix(overrides: Partial<CycloneFix> = {}): CycloneFix {
  return {
    capturedAt: '2021-04-17T00:00:00Z',
    latitude: 10,
    longitude: 130,
    windKnots: 90,
    pressureMillibars: 940,
    classification: 'TY',
    distanceToLandKm: null,
    isLandfall: false,
    eyewallRadiusNm: null,
    outerRadiusNm: null,
    galeGeometry: 'None',
    galeThresholdKnots: 34,
    galeNorthEastNm: null,
    galeSouthEastNm: null,
    galeSouthWestNm: null,
    galeNorthWestNm: null,
    galeLongAxisNm: null,
    galeShortAxisNm: null,
    galeBearingDegrees: null,
    galeAsymmetryRatio: null,
    stormNorthEastNm: null,
    stormSouthEastNm: null,
    stormSouthWestNm: null,
    stormNorthWestNm: null,
    hurricaneNorthEastNm: null,
    hurricaneSouthEastNm: null,
    hurricaneSouthWestNm: null,
    hurricaneNorthWestNm: null,
    ...overrides,
  };
}

/** Builds a track with the given per-component counts, padding to `total` bare fixes. */
function track(
  agency: string,
  averagingPeriod: string,
  total: number,
  withExtent: number,
  withInnerBands: number,
  withEyewall: number,
): AgencyTrack {
  const fixes: CycloneFix[] = [];

  for (let i = 0; i < total; i++) {
    fixes.push(
      fix({
        galeGeometry: i < withExtent ? 'Quadrants' : 'None',
        hurricaneNorthEastNm: i < withInnerBands ? 30 : null,
        eyewallRadiusNm: i < withEyewall ? 17 : null,
      }),
    );
  }

  return {
    agency,
    sourceSlug: agency.toLowerCase().replace(/\s+/g, '-'),
    averagingPeriod,
    peakWindKnots: 170,
    minimumPressureMillibars: 895,
    fixes,
  };
}

// SURIGAE 2021 exactly as the API returns it.
const SURIGAE: readonly AgencyTrack[] = [
  track('China Meteorological Administration', '2-min', 145, 0, 0, 0),
  track('Japan Meteorological Agency', '10-min', 145, 89, 0, 0),
  track('Joint Typhoon Warning Center', '1-min', 111, 89, 63, 111),
  track('Korea Meteorological Administration', '10-min', 95, 87, 0, 0),
  track('Hong Kong Observatory', '10-min', 91, 0, 0, 0),
];

describe('default emphasised agency', () => {
  it('opens on the agency with the most fully described wind field', () => {
    // JTWC, despite having the *fewest* fixes of the three agencies that report extent.
    expect(mostFullyReported(SURIGAE).agency).toBe('Joint Typhoon Warning Center');
  });

  it('does not open on an agency that publishes no extent', () => {
    // The original bug. CMA reports the joint-highest fix count and no wind field whatsoever, so
    // the panel opened with "no footprint is drawn" and the storm appeared as a bare line.
    const chosen = mostFullyReported(SURIGAE);

    expect(chosen.agency).not.toBe('China Meteorological Administration');
    expect(chosen.fixes.some((candidate) => candidate.galeGeometry !== 'None')).toBe(true);
  });

  it('prefers a richer field over a longer track', () => {
    // The second bug. Both report extent at 89 fixes; JMA has 34 more fixes but publishes only an
    // outer ellipse, which cannot produce a wind profile. Fix count must not win here.
    const pair = [
      track('Japan Meteorological Agency', '10-min', 145, 89, 0, 0),
      track('Joint Typhoon Warning Center', '1-min', 111, 89, 63, 111),
    ];

    expect(mostFullyReported(pair).agency).toBe('Joint Typhoon Warning Center');
  });

  it('falls back to fix count when the fields are equally described', () => {
    const pair = [
      track('Shorter', '10-min', 80, 40, 10, 10),
      track('Longer', '10-min', 120, 40, 10, 10),
    ];

    expect(mostFullyReported(pair).agency).toBe('Longer');
  });

  it('still returns a track when no agency reports extent', () => {
    // Common for recent, not-yet-reanalysed seasons. The panel must open on something.
    const pair = [
      track('One', '10-min', 40, 0, 0, 0),
      track('Two', '10-min', 60, 0, 0, 0),
    ];

    expect(mostFullyReported(pair).agency).toBe('Two');
  });

  it('does not reorder the caller\'s collection', () => {
    // The store passes the track list straight from the loaded storm; sorting it in place would
    // silently rearrange the agency list the panel renders.
    const original = [...SURIGAE];

    mostFullyReported(SURIGAE);

    expect(SURIGAE.map((candidate) => candidate.agency)).toStrictEqual(
      original.map((candidate) => candidate.agency),
    );
  });
});

