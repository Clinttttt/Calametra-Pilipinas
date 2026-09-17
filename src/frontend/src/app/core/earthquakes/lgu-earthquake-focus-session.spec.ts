import { describe, expect, it } from 'vitest';

import { LguEarthquakeFocusSession } from './lgu-earthquake-focus-session';

describe('LguEarthquakeFocusSession', () => {
  it('shows the complete contained set and restores timeline and isolation exactly', () => {
    const session = new LguEarthquakeFocusSession();
    const normal = { timeInstantMs: 1_700_000_000_000, isolatedEventId: 'archive-event' };

    expect(session.enter(normal)).toEqual({ timeInstantMs: null, isolatedEventId: null });
    expect(session.exit()).toEqual(normal);
  });

  it('resets a new LGU to all contained events without replacing the saved archive state', () => {
    const session = new LguEarthquakeFocusSession();
    const normal = { timeInstantMs: 1_700_000_000_000, isolatedEventId: 'archive-event' };
    session.enter(normal);

    expect(session.resetForAnotherLgu()).toEqual({ timeInstantMs: null, isolatedEventId: null });
    expect(session.exit()).toEqual(normal);
  });
});
