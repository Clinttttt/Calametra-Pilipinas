import { EarthquakeFilterStore } from './earthquake-filter-store';

/**
 * The filter's window arithmetic and its own account of whether it is filtering.
 *
 * Worth testing rather than eyeballing for two reasons. Calendar boundaries are the classic place
 * for an off-by-one — "this month" computed as thirty days back silently includes the tail of the
 * previous month — and `isFiltered` drives the wording of the count in the corner, so if it is wrong
 * the interface states a subset as a total, which is the one thing this platform is built not to do.
 */
describe('EarthquakeFilterStore', () => {
  const store = (): EarthquakeFilterStore => new EarthquakeFilterStore();

  /** Mid-month, mid-year, and in the evening UTC so a local-time slip would move the date. */
  const now = new Date(Date.UTC(2026, 8, 12, 22, 30));

  it('opens on the era-comparable record rather than the whole archive', () => {
    const filter = store();

    // 27,241 events across 125 years drawn at once read as a smear, and the sparse early record
    // invites the false conclusion that earthquakes are becoming more frequent.
    expect(filter.minMagnitude()).toBe(6);
    expect(filter.fromMs()).toBeNull();
    expect(filter.isFiltered()).toBe(true);
  });

  it('starts this month at the first of the month, not thirty days ago', () => {
    const filter = store();

    filter.applyPreset('this-month', now);

    expect(filter.fromMs()).toBe(Date.UTC(2026, 8, 1));
    // No upper bound: every preset means "since", and the scrubber owns the upper edge.
    expect(filter.toMs()).toBeNull();
  });

  it('starts this year at the first of January', () => {
    const filter = store();

    filter.applyPreset('this-year', now);

    expect(filter.fromMs()).toBe(Date.UTC(2026, 0, 1));
  });

  it('counts twelve months back from the same day, not from the start of the month', () => {
    const filter = store();

    filter.applyPreset('twelve-months', now);

    expect(filter.fromMs()).toBe(Date.UTC(2025, 8, 12, 22, 30));
  });

  it('computes the window in UTC, so an evening here does not shift the date', () => {
    const filter = store();

    // 22:30 UTC on 12 September is 06:30 on the 13th in Manila. Computed locally, "this month"
    // would still land on the 1st — but "twelve months" would land on the 13th, and the archive's
    // own instants are UTC.
    filter.applyPreset('twelve-months', now);

    expect(new Date(filter.fromMs()!).getUTCDate()).toBe(12);
  });

  it('reports no filtering once every bound is cleared', () => {
    const filter = store();

    filter.applyPreset('this-year', now);
    filter.setDepthRange(0, 70);
    filter.setIncludeAssignedDepth(false);

    expect(filter.isFiltered()).toBe(true);

    filter.clear();

    expect(filter.isFiltered()).toBe(false);
    expect(filter.minMagnitude()).toBeNull();
    expect(filter.includeAssignedDepth()).toBe(true);
    expect(filter.preset()).toBe('all');
  });

  it('treats excluding assigned depths as a filter in its own right', () => {
    const filter = store();

    filter.clear();
    filter.setIncludeAssignedDepth(false);

    // 43% of the archive carries a depth the agency assigned rather than measured, so hiding them
    // changes the count materially and the interface has to say so.
    expect(filter.isFiltered()).toBe(true);
  });

  it('round-trips every filter field and its named preset', () => {
    const filter = store();
    filter.applyPreset('five-years', now);
    filter.setMagnitudeRange(4.2, 7.1);
    filter.setDepthRange(12, 90);
    filter.setIncludeAssignedDepth(false);
    const snapshot = filter.snapshot();

    filter.clear();
    filter.restore(snapshot);

    expect(filter.snapshot()).toEqual(snapshot);
  });
});
