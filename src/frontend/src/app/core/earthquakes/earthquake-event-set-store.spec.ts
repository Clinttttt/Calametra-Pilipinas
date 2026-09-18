import { EarthquakeEventSetStore } from './earthquake-event-set-store';
import { HazardModeStore } from '../hazards/hazard-mode-store';

describe('EarthquakeEventSetStore', () => {
  it('starts with Earthquakes able to be active but no event population selected', () => {
    const store = new EarthquakeEventSetStore();
    const hazards = new HazardModeStore();

    hazards.select('earthquakes');

    expect(hazards.isEarthquakes()).toBe(true);
    expect(store.eventSet()).toBe('none');
    expect(store.hasDisplayedPopulation()).toBe(false);
  });

  it('keeps an executed zero-result filter distinct from the idle state', () => {
    const store = new EarthquakeEventSetStore();

    store.select('customFiltered');

    // Result cardinality belongs to the executed query/filter; it must not collapse the population
    // state back to `none` merely because the count happens to be zero.
    expect(store.eventSet()).toBe('customFiltered');
    expect(store.hasDisplayedPopulation()).toBe(true);
  });

  it('activates a filtered population only when the reader applies a meaningful filter', () => {
    const store = new EarthquakeEventSetStore();

    store.selectFilterResult(true);
    expect(store.eventSet()).toBe('customFiltered');

    store.selectFilterResult(false);
    expect(store.eventSet()).toBe('completeCatalogue');
  });

  it.each(['historicalComparable', 'customFiltered', 'completeCatalogue', 'isolatedEvent', 'lguFocus'] as const)(
    'recognises %s as a displayed population',
    (eventSet) => {
      const store = new EarthquakeEventSetStore();

      store.select(eventSet);

      expect(store.eventSet()).toBe(eventSet);
      expect(store.hasDisplayedPopulation()).toBe(true);
    },
  );
});
