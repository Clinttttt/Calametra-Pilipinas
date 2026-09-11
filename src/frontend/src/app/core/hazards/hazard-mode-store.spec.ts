import { describe, expect, it } from 'vitest';

import { HazardModeStore } from './hazard-mode-store';

/**
 * The opening state, and the guarantee that nothing is plotted until the reader chooses.
 *
 * `choosing` is the signal the map keys its whole initial view off: the chooser card, the empty map,
 * the absent tool rail and the deferred archive fetch all follow from it. If it ever started false
 * the platform would silently return to opening on a dataset nobody asked for, so it is asserted
 * directly rather than left to the template.
 */
describe('hazard mode', () => {
  it('starts with nothing selected, so nothing is plotted', () => {
    const store = new HazardModeStore();

    expect(store.selected()).toBeNull();
    expect(store.choosing()).toBe(true);
    expect(store.isEarthquakes()).toBe(false);
    expect(store.isCyclones()).toBe(false);
    expect(store.active()).toBeNull();
  });

  it('activates exactly one hazard at a time', () => {
    const store = new HazardModeStore();

    store.select('earthquakes');

    expect(store.isEarthquakes()).toBe(true);
    expect(store.isCyclones()).toBe(false);
    expect(store.choosing()).toBe(false);

    store.select('cyclones');

    // Mutually exclusive, not additive. Two hazards drawn at once would put a depth ramp and a wind
    // ramp on the same map with no way to tell which a colour belonged to.
    expect(store.isEarthquakes()).toBe(false);
    expect(store.isCyclones()).toBe(true);
  });

  it('resolves the active option for the switcher label', () => {
    const store = new HazardModeStore();

    store.select('cyclones');

    expect(store.active()?.label).toBe('Tropical cyclones');
    expect(store.active()?.mode).toBe('cyclones');
  });

  it('returns to the chooser when cleared', () => {
    const store = new HazardModeStore();

    store.select('earthquakes');
    store.clear();

    expect(store.choosing()).toBe(true);
    expect(store.active()).toBeNull();
  });

  it('offers every hazard with a summary and a stated coverage', () => {
    // Coverage must never be blank: it is the only place the chooser says whose record this is and
    // how far back it reaches, and an unattributed archive is exactly what this platform exists to
    // avoid presenting.
    expect(HazardModeStore.options).toHaveLength(2);

    for (const option of HazardModeStore.options) {
      expect(option.summary.length, option.mode).toBeGreaterThan(0);
      expect(option.coverage, option.mode).toMatch(/\d{4}/);
      expect(option.icon.length, option.mode).toBeGreaterThan(0);
    }
  });

  it('has a distinct option per mode', () => {
    const modes = HazardModeStore.options.map((option) => option.mode);

    expect(new Set(modes).size).toBe(modes.length);
  });
});
