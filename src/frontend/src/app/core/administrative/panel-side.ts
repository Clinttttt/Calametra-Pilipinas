/**
 * Which side of the map a panel should occupy so it does not cover what the reader just clicked.
 *
 * **Why this is a decision and not a fixed rail.** A municipality outline can fill most of the viewport,
 * and the panel describing it is opaque enough to read. Anchored permanently to the right, a click on the
 * right-hand side of the map puts the answer directly on top of the thing being answered about — which is
 * the one placement guaranteed to be wrong.
 *
 * The rule is deliberately coarse: the panel goes to the far side of whichever half was clicked. A finer
 * rule — tracking the selected geometry's bounds, or following the cursor — would move the panel while the
 * reader is reading it, and a panel that relocates under the eye is worse than one that is occasionally
 * further from the subject than it needed to be.
 */

/** Which side the panel is placed on. */
export type PanelSide = 'left' | 'right';

/**
 * Chooses the side opposite the click.
 *
 * @param normalisedX Click position across the map, 0 at the left edge and 1 at the right.
 */
export function panelSideFor(normalisedX: number | null): PanelSide {
  // Right by default, which is where every other panel in Explore lives: with nothing clicked there is
  // nothing to avoid, and consistency is worth more than symmetry.
  if (normalisedX === null || Number.isNaN(normalisedX)) {
    return 'right';
  }

  // Past the midline the reader clicked the right half, so the panel takes the left. Exactly on the
  // midline it stays right, so a click on the centre line does not flip the panel back and forth.
  return normalisedX > 0.5 ? 'left' : 'right';
}
