import { describe, expect, it } from 'vitest';

import { LGU_MAX_ZOOM } from '../layers/lgu-boundary-source';
import {
  LGU_BAND_LOCAL,
  LGU_BAND_REGIONAL,
  LGU_LINE_OPACITY,
  LGU_LINE_WIDTH,
  lguBand,
  lguBoundariesVisibleAt,
  lguInteractiveAt,
} from './lgu-zoom-bands';

describe('LGU zoom bands', () => {
  it('draws nothing at national zoom', () => {
    // ADR-005 D6. Sixteen hundred outlines at this zoom is a grey mesh with the country lost inside it.
    expect(lguBoundariesVisibleAt(3)).toBe(false);
    expect(lguBoundariesVisibleAt(5)).toBe(false);
    expect(lguBand(4)).toBe('national');
  });

  it('draws faintly from the regional band', () => {
    expect(lguBoundariesVisibleAt(LGU_BAND_REGIONAL)).toBe(true);
    expect(lguBand(LGU_BAND_REGIONAL)).toBe('regional');
    expect(lguBand(8)).toBe('regional');
  });

  it('becomes clear locally', () => {
    expect(lguBand(LGU_BAND_LOCAL)).toBe('local');
    expect(lguBand(13)).toBe('local');
  });

  it('is not interactive until an outline is big enough to aim at', () => {
    // Below the local band a click resolves to whichever of several small units is under the cursor,
    // which is worse than not answering.
    expect(lguInteractiveAt(7)).toBe(false);
    expect(lguInteractiveAt(8)).toBe(false);
    expect(lguInteractiveAt(LGU_BAND_LOCAL)).toBe(true);
    expect(lguInteractiveAt(12)).toBe(true);
  });

  it('starts drawing exactly where tiles start being served', () => {
    // Drawing below the tile floor would show an empty layer and no explanation.
    expect(LGU_BAND_REGIONAL).toBe(6);
  });

  it('grows opacity monotonically across the bands', () => {
    const stops = LGU_LINE_OPACITY.slice(3).filter((_, index) => index % 2 === 1) as number[];

    expect(stops).toEqual([...stops].sort((left, right) => left - right));
    expect(stops[0]).toBeLessThan(0.25);
    expect(stops[stops.length - 1]).toBeLessThanOrEqual(1);
  });

  it('grows width monotonically and stays a hairline regionally', () => {
    const stops = LGU_LINE_WIDTH.slice(3).filter((_, index) => index % 2 === 1) as number[];

    expect(stops).toEqual([...stops].sort((left, right) => left - right));
    expect(stops[0]).toBeLessThan(0.5);
  });

  it('interpolates between the two bands and the deepest served zoom', () => {
    expect(LGU_LINE_OPACITY).toContain(LGU_BAND_REGIONAL);
    expect(LGU_LINE_OPACITY).toContain(LGU_BAND_LOCAL);
    expect(LGU_LINE_OPACITY).toContain(LGU_MAX_ZOOM);
  });
});
