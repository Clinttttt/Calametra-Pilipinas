import { describe, expect, it } from 'vitest';

import { LGU_MAX_ZOOM } from '../layers/lgu-boundary-source';
import {
  LGU_BAND_LOCAL,
  LGU_BAND_REGIONAL,
  LGU_OPACITY_STOPS,
  LGU_WIDTH_STOPS,
  lguBand,
  lguBoundariesVisibleAt,
  lguInteractiveAt,
  lguLineOpacityExpression,
  lguLineWidthExpression,
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
    const stops = [...LGU_OPACITY_STOPS];

    expect(stops).toEqual([...stops].sort((left, right) => left - right));
    expect(stops[0]).toBeLessThan(0.25);
    expect(stops[stops.length - 1]).toBeLessThanOrEqual(1);
  });

  it('grows width monotonically and stays a hairline regionally', () => {
    const stops = [...LGU_WIDTH_STOPS];

    expect(stops).toEqual([...stops].sort((left, right) => left - right));
    expect(stops[0]).toBeLessThan(0.5);
  });

  it('interpolates on zoom as the outermost expression', () => {
    // MapLibre rejects a paint property whose zoom reference sits inside another expression, which is
    // why fault-style makes the same point. Asserted rather than assumed, because the failure is a
    // thrown error at style load rather than a wrong colour.
    const opacity = lguLineOpacityExpression();

    expect(opacity[0]).toBe('interpolate');
    expect(opacity[2]).toEqual(['zoom']);
    expect(opacity).toContain(LGU_BAND_REGIONAL);
    expect(opacity).toContain(LGU_BAND_LOCAL);
    expect(opacity).toContain(LGU_MAX_ZOOM);
  });

  it('builds the width expression the same way', () => {
    const width = lguLineWidthExpression();

    expect(width[0]).toBe('interpolate');
    expect(width[2]).toEqual(['zoom']);
  });
});
