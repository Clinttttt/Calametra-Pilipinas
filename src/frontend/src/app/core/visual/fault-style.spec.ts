import {
  describeSlipType,
  faultCasingWidthExpression,
  faultLineWidthExpression,
} from './fault-style';

/**
 * The one structural rule these expressions have to obey, and the vocabulary rule they must not
 * break.
 *
 * <b>Why this is worth a test.</b> MapLibre requires a `zoom` reference to be the outermost
 * expression in a paint property, and it enforces that by throwing from `addLayer`. Because the map
 * component treats a throw as "this layer could not be loaded", a nested `zoom` presents as a layer
 * that draws nothing at all — no error on screen, no clue where to look. That has now happened twice
 * in this project: once to the earthquake marker radius, and once to the fault width, where a
 * multiplier for subduction thrusts wrapped the whole zoom curve in a `case`. The rule is asserted
 * here so a third occurrence fails in CI instead of on the map.
 */
describe('fault styling', () => {
  const zoomIsOutermost = (expression: unknown[]): boolean =>
    expression[0] === 'interpolate'
    && Array.isArray(expression[2])
    && expression[2][0] === 'zoom';

  it('keeps zoom as the outermost expression in both widths', () => {
    expect(zoomIsOutermost(faultCasingWidthExpression())).toBe(true);
    expect(zoomIsOutermost(faultLineWidthExpression())).toBe(true);
  });

  it('never references zoom below the top level', () => {
    // A nested reference is the failure mode, so it is searched for rather than assumed absent.
    const nestedZoom = (node: unknown, depth = 0): boolean => {
      if (!Array.isArray(node)) {
        return false;
      }

      if (depth > 0 && node[0] === 'zoom') {
        return true;
      }

      return node.some((child) => nestedZoom(child, depth + 1));
    };

    // Depth 1 is the legitimate `['zoom']` operand of the interpolation itself, so the search starts
    // inside each stop.
    for (const expression of [faultCasingWidthExpression(), faultLineWidthExpression()]) {
      const stops = expression.slice(3);

      expect(stops.some((stop) => nestedZoom(stop))).toBe(false);
    }
  });

  it('widens subduction thrusts at every stop rather than multiplying the curve', () => {
    const stops = faultCasingWidthExpression().slice(3);
    const branches = stops.filter((stop): stop is unknown[] => Array.isArray(stop));

    // One branch per zoom stop: a plate interface is a different kind of structure from a crustal
    // break, and the distinction has to survive the restructuring that fixed the nesting.
    expect(branches).toHaveLength(3);
    branches.forEach((branch) => expect(branch[0]).toBe('case'));
  });

  it("presents the publisher's slip type tidied but not translated", () => {
    // Substituting our vocabulary for a geological authority's misrepresents the source: a reader
    // checking against GEM's own record has to find the same word.
    expect(describeSlipType('Sinistral_Reverse')).toBe('Sinistral Reverse');
    expect(describeSlipType('Subduction_Thrust')).toBe('Subduction Thrust');
    expect(describeSlipType(null)).toBe('Slip type not recorded');
  });
});
