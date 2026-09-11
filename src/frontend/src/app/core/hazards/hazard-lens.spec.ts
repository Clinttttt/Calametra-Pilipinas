import { describe, expect, it } from 'vitest';

import { HazardModeStore } from './hazard-mode-store';

/**
 * Every hazard must name the layer lens it owns.
 *
 * This is the third time earthquake content leaked into the cyclone view — first the cross-section
 * presets, then the panels, then the published overlays — so the mapping is asserted rather than
 * trusted. The failure mode is quiet: a wrong or missing lens shows PHIVOLCS fault traces over a
 * storm track and nothing errors.
 */
describe('hazard layer lenses', () => {
  it('gives every hazard a lens', () => {
    for (const option of HazardModeStore.options) {
      expect(option.lens, option.mode).toBeTruthy();
    }
  });

  it('does not share a lens between hazards', () => {
    // Two hazards on one lens would mean switching hazard left the other's overlays switched on,
    // because `applyLens` keys the visible set on the lens alone.
    const lenses = HazardModeStore.options.map((option) => option.lens);

    expect(new Set(lenses).size).toBe(lenses.length);
  });

  it('maps earthquakes to Seismic and cyclones to Cyclone', () => {
    // The concrete expectation. Every layer seeded today is Seismic, verified against
    // /api/hazard-layers: three entries, two enabled by default. So earthquakes must claim Seismic,
    // or the fault traces become unreachable — and cyclones must not, or they inherit them.
    const byMode = new Map(HazardModeStore.options.map((option) => [option.mode, option.lens]));

    expect(byMode.get('earthquakes')).toBe('Seismic');
    expect(byMode.get('cyclones')).toBe('Cyclone');
  });
});
