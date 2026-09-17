/** Transient map narrowing that sits outside the durable earthquake filter store. */
export interface EarthquakeTransientView {
  readonly timeInstantMs: number | null;
  readonly isolatedEventId: string | null;
}

const COMPLETE_CONTAINED_VIEW: EarthquakeTransientView = {
  timeInstantMs: null,
  isolatedEventId: null,
};

/**
 * Lossless transaction around the component-local timeline scrub and event isolation.
 * Durable magnitude/time-window/depth filters are snapshotted by LguEarthquakeMapScopeStore.
 */
export class LguEarthquakeFocusSession {
  private normalView: EarthquakeTransientView | null = null;

  get active(): boolean {
    return this.normalView !== null;
  }

  enter(normalView: EarthquakeTransientView): EarthquakeTransientView {
    this.normalView ??= { ...normalView };
    return COMPLETE_CONTAINED_VIEW;
  }

  resetForAnotherLgu(): EarthquakeTransientView {
    return COMPLETE_CONTAINED_VIEW;
  }

  exit(): EarthquakeTransientView {
    const restored = this.normalView ?? COMPLETE_CONTAINED_VIEW;
    this.normalView = null;
    return restored;
  }
}
