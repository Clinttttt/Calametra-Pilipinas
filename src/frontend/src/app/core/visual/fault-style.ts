/**
 * FAULT LAYER STYLING
 *
 * Faults are structural *context*, not the measurement. Earthquakes are the data and
 * they already own hue through the depth ramp, so a saturated fault line would put two
 * things in competition and make both harder to read.
 *
 * The technique here is line casing — the standard cartographic answer to drawing a
 * line over a busy background. A dark wide stroke underneath and a light narrow stroke
 * on top gives the line its own edge, so it stays legible over deep ocean, lit terrain
 * and a dense cluster of markers alike, without needing a colour loud enough to fight
 * for attention.
 *
 * Slip type is deliberately *not* encoded as colour. There are seven slip types in the
 * Philippine set, and seven more hues would overwhelm a map that already encodes depth
 * in five. Subduction thrusts get extra width because they are plate boundaries rather
 * than crustal faults — a difference of kind, not degree — and everything else is
 * revealed on inspection.
 */

/** Casing: a dark stroke beneath the fault, giving it a defined edge. */
export const FAULT_CASING_COLOUR = '#05080c';

/** The fault line itself. A warm off-white, so it reads as drawn rather than lit. */
export const FAULT_LINE_COLOUR = '#d8d2c8';

/** Subduction thrust segments, drawn slightly warmer and wider. */
export const TRENCH_LINE_COLOUR = '#c9bda8';

/**
 * Line width for the fault casing, by zoom, with subduction thrusts already widened.
 *
 * Faults are barely useful at national zoom — 155 traces across 1,800 km is visual
 * noise — so they stay thin until the user is close enough for them to mean something.
 *
 * <b>The zoom interpolation is the outermost expression, and it has to be.</b> MapLibre rejects a
 * paint property whose `zoom` reference sits inside another expression, and it rejects it by throwing
 * from `addLayer` rather than by degrading — which, because the caller treats a throw as "this layer
 * failed", presents as a layer that silently does nothing. This function previously returned the bare
 * zoom curve and a separate wrapper multiplied it by 1.7 for thrusts, which put `zoom` inside a
 * `case` inside a `*`. The multiplication is now folded into each stop instead. The marker radius
 * carries the same note for the same reason.
 */
export function faultCasingWidthExpression(): unknown[] {
  return widthByZoom([
    [5, 1.6],
    [8, 2.6],
    [12, 4.5],
  ]);
}

export function faultLineWidthExpression(): unknown[] {
  return widthByZoom([
    [5, 0.5],
    [8, 1.1],
    [12, 2.2],
  ]);
}

/** Subduction thrusts are drawn wider, being plate boundaries rather than crustal faults. */
const THRUST_WIDTH_MULTIPLIER = 1.7;

/**
 * Builds a zoom-outermost width curve whose stops branch on slip type.
 *
 * Each stop is a `case` rather than the whole curve being multiplied, so `zoom` stays at the top
 * level. More verbose than the arithmetic it replaces, and that verbosity is the point.
 */
function widthByZoom(stops: readonly (readonly [number, number])[]): unknown[] {
  const expression: unknown[] = ['interpolate', ['linear'], ['zoom']];

  for (const [zoom, width] of stops) {
    expression.push(zoom, [
      'case',
      ['==', ['coalesce', ['get', 'classification'], ''], 'Subduction_Thrust'],
      width * THRUST_WIDTH_MULTIPLIER,
      width,
    ]);
  }

  return expression;
}

/**
 * Colour expression distinguishing plate boundaries from crustal faults.
 *
 * A subduction thrust is a plate interface; a strike-slip fault is a crustal break.
 * Treating them identically would be tidier and wrong.
 */
export function faultColourExpression(): unknown[] {
  return [
    'case',
    ['==', ['coalesce', ['get', 'classification'], ''], 'Subduction_Thrust'],
    TRENCH_LINE_COLOUR,
    FAULT_LINE_COLOUR,
  ];
}

/**
 * Human-readable slip type.
 *
 * The publisher's values arrive with underscores and inconsistent casing. Presented
 * tidied but never renamed — `Sinistral` stays `Sinistral` rather than becoming
 * "left-lateral", because substituting our vocabulary for a geological authority's
 * misrepresents the source.
 */
export function describeSlipType(classification: string | null): string {
  if (!classification) {
    return 'Slip type not recorded';
  }

  return classification.replace(/_/g, ' ');
}
