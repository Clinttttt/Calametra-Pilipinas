import type { CycloneDecade, DecadeSummary, PlaceContext } from '../../core/api/contracts';

/**
 * WHAT STANDS OUT — COMPOSED, NOT WRITTEN
 *
 * These sentences are the only place on the Compare page where the platform states a conclusion in
 * prose, so they are held here as pure functions and unit-tested. A number rendered wrongly is a
 * visible defect; a sentence that overstates what the archive supports is an invisible one.
 *
 * Three rules govern every line:
 *
 * <b>1. Claims are about the record, never about the world.</b> "Surigao's archive holds more M6.0+
 * observations" is true. "Surigao has more M6.0+ earthquakes" is a claim about seismicity that a
 * catalogue with a detection history cannot support, and the difference is the whole point of this
 * page.
 *
 * <b>2. Only comparable series are compared.</b> The M6.0+ count and the landfall count carry the
 * comparison; raw catalogue totals appear only inside a statement about instrumentation.
 *
 * <b>3. The closing line is always a limit, not a finding.</b> It is rendered in the caution colour
 * and it is what stops the list reading as a verdict.
 *
 * No superlatives, no ranking words, and no "more dangerous" in any branch.
 */

/** Which side holds the larger value, or null when they are equal. */
function lead(left: number, right: number): 'left' | 'right' | null {
  if (left === right) {
    return null;
  }

  return left > right ? 'left' : 'right';
}

/** `4.6×`, or `about as many` when the two are within a tenth. */
function ratio(value: number, against: number): string {
  if (against === 0) {
    return 'immeasurably more';
  }

  const factor = value / against;

  return Math.abs(factor - 1) < 0.1 ? 'about as many' : `${factor.toFixed(1)}×`;
}

function floor(comparableMagnitude: number): string {
  return `M${comparableMagnitude.toFixed(1)}+`;
}

/**
 * Two or three observations about a pair of places, then the limit.
 *
 * Ordered by how well the archive supports them: the comparable earthquake series first, the storm
 * series second, the fault proximity third — proximity being the weakest, since an unmapped fault is
 * not an absent one.
 */
export function placeObservations(
  left: PlaceContext,
  right: PlaceContext,
  radiusKm: number,
  comparableMagnitude: number,
): readonly string[] {
  const observations: string[] = [];
  const quakes = lead(left.eventsAtComparableMagnitude, right.eventsAtComparableMagnitude);

  if (quakes === null) {
    observations.push(
      `Both records hold ${left.eventsAtComparableMagnitude} ${floor(comparableMagnitude)} earthquakes within ${radiusKm} km — the comparable series does not separate them.`,
    );
  } else {
    const ahead = quakes === 'left' ? left : right;
    const behind = quakes === 'left' ? right : left;

    observations.push(
      `${ahead.name}'s archive holds more ${floor(comparableMagnitude)} earthquake observations within ${radiusKm} km — ${ahead.eventsAtComparableMagnitude} against ${behind.eventsAtComparableMagnitude} for ${behind.name}.`,
    );
  }

  const storms = lead(left.cyclones.landfallCount, right.cyclones.landfallCount);

  if (storms !== null) {
    const ahead = storms === 'left' ? left : right;
    const behind = storms === 'left' ? right : left;

    observations.push(
      `${ahead.name} has more nearby tracks recorded crossing land — ${ahead.cyclones.landfallCount} against ${behind.cyclones.landfallCount}.`,
    );
  }

  const leftFault = left.nearestFaults.at(0);
  const rightFault = right.nearestFaults.at(0);

  if (leftFault !== undefined && rightFault !== undefined) {
    observations.push(
      `Nearest mapped trace: ${leftFault.name} at ${leftFault.distanceKm} km for ${left.name}, ${rightFault.name} at ${rightFault.distanceKm} km for ${right.name}.`,
    );
  }

  observations.push(
    'Neither place is more hazardous than the other on this evidence. Raw catalogue totals are not occurrence rates, storm counts are track proximity rather than impact, and an unmapped fault is not an absent one.',
  );

  return observations;
}

/**
 * Observations about a pair of decades, then the limit.
 *
 * The first line deliberately puts the total and the comparable rate in one sentence, because the gap
 * between them <em>is</em> the finding: readings rise 285-fold across the century while the M6.0+ rate
 * stays near five a year.
 */
export function eraObservations(
  left: DecadeSummary,
  right: DecadeSummary,
  leftStorms: CycloneDecade | null,
  rightStorms: CycloneDecade | null,
  comparableMagnitude: number,
): readonly string[] {
  const observations: string[] = [
    `The catalogue holds ${ratio(right.totalCount, left.totalCount)} readings in the ${right.decade}s as in the ${left.decade}s, while the ${floor(comparableMagnitude)} rate moves from ${left.comparablePerYear} to ${right.comparablePerYear} a year.`,
  ];

  if (left.dominantScale !== right.dominantScale) {
    observations.push(
      `The dominant magnitude scale changes — ${left.dominantScale ?? 'unstated'} in the ${left.decade}s against ${right.dominantScale ?? 'unstated'} in the ${right.decade}s — so the two sets of magnitudes are not the same kind of number.`,
    );
  }

  if (leftStorms !== null && rightStorms !== null) {
    observations.push(
      `Landfalling storms: ${leftStorms.landfallCount} in the ${left.decade}s against ${rightStorms.landfallCount} in the ${right.decade}s, out of ${leftStorms.stormCount} and ${rightStorms.stormCount} tracked.`,
    );
  }

  observations.push(
    `The rise in readings is instrumentation, not seismicity. Only the ${floor(comparableMagnitude)} series may be compared between eras.`,
  );

  return observations;
}

/**
 * The share of depths actually resolved. Null when there is nothing to divide.
 *
 * Separate from the count it is derived from because an empty radius must render as "no readings"
 * rather than as 0% measured — which would state a fact about depth quality where none exists.
 */
export function measuredShare(total: number, assigned: number): number | null {
  return total === 0 ? null : (total - assigned) / total;
}

/**
 * The share of a paired proportional bar, scaled to the larger of the two values.
 *
 * Scaled between these two subjects rather than to a fixed maximum, because the comparison is between
 * them and nothing else. Zero when both are zero, so an empty pair draws nothing rather than two full
 * bars.
 */
export function barShare(value: number | null, other: number | null): number {
  const peak = Math.max(value ?? 0, other ?? 0);

  return peak === 0 ? 0 : Math.round(((value ?? 0) / peak) * 100);
}
