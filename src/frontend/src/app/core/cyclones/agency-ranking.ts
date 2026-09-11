import type { AgencyTrack } from '../api/contracts';

/**
 * WHICH AGENCY'S READING TO OPEN WITH
 *
 * Its own module, deliberately. This is pure logic over the API contract and has no Angular
 * dependency, whereas `cyclone-store.ts` is an injectable that pulls in the HTTP client — importing
 * the store from a unit test fails at module load with `JIT compilation failed for service [class
 * BrowserXhr]` unless the whole Angular testing harness is stood up. Keeping the decision here
 * means the rule can be tested against real storm profiles for the cost of a plain import, which is
 * what it needs, because it has now been got wrong twice.
 */

/**
 * The agency whose reading of the storm is the most complete, used as the opening default.
 *
 * The API orders tracks by fix count, and taking the first was the original rule. It chose badly:
 * for SURIGAE 2021 the China Meteorological Administration and the Japan Meteorological Agency both
 * report 145 fixes, CMA sorts first, and CMA publishes **no wind extent at all** — so the panel
 * opened saying "this agency reported intensity but not extent, so no footprint is drawn" and the
 * reader met the storm as a bare line. The most detailed *count* is not the most detailed *reading*.
 *
 * Testing for the mere presence of extent was not enough either. On SURIGAE both JMA and JTWC report
 * extent at 89 fixes, so a presence test tie-broke on fix count and chose JMA's 145 over JTWC's 111
 * — but JMA publishes a single ellipse with no inner bands and no radius of maximum wind, which
 * renders as one flat ring, while JTWC publishes quadrant radii at 34 kt, the inner 50 and 64 kt
 * bands at 63 fixes and an eyewall radius at 111. Only the latter carries enough sample points to
 * reconstruct a wind profile at all. So each component of the field is counted separately.
 *
 * This is a default, not a claim of authority: every agency stays listed with its own reading, the
 * others remain drawn on the map, and one click changes the emphasis. Choosing which reading to open
 * with is unavoidable — there is no null option that shows nothing — so it is made explicitly and on
 * a stated criterion rather than by whatever the sort happened to put first.
 */
export function mostFullyReported(tracks: readonly AgencyTrack[]): AgencyTrack {
  const scored = tracks.map((candidate) => ({
    candidate,
    // Each component counted separately, so an agency publishing quadrants *and* inner bands *and*
    // an eyewall outranks one publishing only an outer ellipse at more fixes.
    detail:
      candidate.fixes.filter((fix) => fix.galeGeometry !== 'None').length
      + candidate.fixes.filter((fix) => fix.hurricaneNorthEastNm !== null).length
      + candidate.fixes.filter((fix) => fix.eyewallRadiusNm !== null).length,
  }));

  // Sorts the wrapper array built above, so the caller's collection is never reordered — the store
  // passes the loaded storm's track list straight in, and the panel renders that same list.
  scored.sort((a, b) => b.detail - a.detail || b.candidate.fixes.length - a.candidate.fixes.length);

  return scored[0].candidate;
}
