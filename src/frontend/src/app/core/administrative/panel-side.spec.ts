import { describe, expect, it } from 'vitest';

import { panelSideFor } from './panel-side';

describe('panelSideFor', () => {
  it('takes the left when the reader clicked the right half', () => {
    // The placement guaranteed to be wrong is the answer sitting on top of the thing it answers about,
    // and a municipality outline can fill most of the viewport.
    expect(panelSideFor(0.8)).toBe('left');
    expect(panelSideFor(0.51)).toBe('left');
  });

  it('stays on the right when the reader clicked the left half', () => {
    expect(panelSideFor(0.2)).toBe('right');
    expect(panelSideFor(0.49)).toBe('right');
  });

  it('does not flip on the midline', () => {
    // Otherwise a click on the centre line would move the panel back and forth between two clicks that
    // look identical to the reader.
    expect(panelSideFor(0.5)).toBe('right');
  });

  it('defaults to the right when nothing has been clicked', () => {
    // Where every other panel in Explore lives. With nothing selected there is nothing to avoid, and
    // consistency is worth more than symmetry.
    expect(panelSideFor(null)).toBe('right');
  });

  it('defaults to the right rather than throwing on a bad measurement', () => {
    // A zero-width canvas during layout would divide to NaN. The panel appearing on its usual side is a
    // better failure than a panel that does not appear.
    expect(panelSideFor(Number.NaN)).toBe('right');
  });
});
