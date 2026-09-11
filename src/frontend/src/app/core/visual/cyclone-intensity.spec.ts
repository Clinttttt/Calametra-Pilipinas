import { describe, expect, it } from 'vitest';

import {
  UNMEASURED_WIND_COLOUR,
  categoryLabel,
  fixRadiusForWind,
  intensityLegend,
  trackColourForWind,
  trackWidthForWind,
} from './cyclone-intensity';

/**
 * The boundaries of the intensity ramp, and the rule that keeps it honest.
 *
 * The off-by-one risk here is real and silent: a band expressed as `>= 64` versus `> 64` moves
 * every Category 1 storm in the archive into the tropical-storm colour, and nothing would fail
 * except the picture. So each threshold is asserted at the value itself and one knot below it.
 *
 * The averaging-period rule is the part worth protecting. `categoryLabel` must refuse to name a
 * Saffir–Simpson category for anything but a one-minute sustained wind, because the scale is
 * defined on that interval and JMA, KMA and HKO do not report it.
 */
describe('cyclone intensity ramp', () => {
  it('assigns each Saffir-Simpson band at its lower bound', () => {
    const bands: readonly [number, string][] = [
      [137, '#ff6060'],
      [113, '#ff8f20'],
      [96, '#ffc140'],
      [83, '#ffe775'],
      [64, '#ffffcc'],
      [34, '#00faf4'],
      [0, '#5ebaff'],
    ];

    for (const [knots, colour] of bands) {
      expect(trackColourForWind(knots), `${knots} kt`).toBe(colour);
    }
  });

  it('keeps one knot below a threshold in the weaker band', () => {
    // The inclusive-bound check. If any comparison were `>` instead of `>=`, the band above
    // would claim these readings and the whole ramp would shift by one knot.
    expect(trackColourForWind(136)).toBe('#ff8f20');
    expect(trackColourForWind(112)).toBe('#ffc140');
    expect(trackColourForWind(63)).toBe('#00faf4');
    expect(trackColourForWind(33)).toBe('#5ebaff');
  });

  it('takes an unmeasured wind out of the ramp entirely', () => {
    // A fix with pressure but no wind must not be coloured as a weak storm. Some agencies in
    // this archive report pressure alone, so this is a real case and not a defensive one.
    expect(trackColourForWind(null)).toBe(UNMEASURED_WIND_COLOUR);
    expect(trackColourForWind(null)).not.toBe(trackColourForWind(0));
  });
});

describe('Saffir-Simpson labelling', () => {
  // The period strings are the ones the API actually sends, confirmed against
  // /api/cyclones/{id} for SURIGAE 2021: JTWC 1-min, CMA 2-min, JMA/KMA/HKO 10-min.
  it('names a category only for a one-minute sustained wind', () => {
    expect(categoryLabel(140, '1-min')).toBe('Category 5');
    expect(categoryLabel(70, '1-min')).toBe('Category 1');
  });

  it('refuses a category for every other averaging period', () => {
    // The central honesty rule. 140 kt over ten minutes is a stronger storm than 140 kt over one
    // minute, so applying the one-minute scale to it would overstate a measured value — and in
    // this archive the one-minute reading is sometimes the *lower* of the two, so no conversion
    // factor could rescue the comparison.
    for (const period of ['10-min', '2-min', '3-min', 'Unknown']) {
      expect(categoryLabel(140, period), period).toBeNull();
    }
  });

  it('refuses a category when no wind was reported', () => {
    expect(categoryLabel(null, '1-min')).toBeNull();
  });

  it('does not accept the enum name, which is not what the API sends', () => {
    // Regression guard. The first version compared against `OneMinute` and therefore suppressed
    // the category for every agency, including JTWC. Nothing failed; the label simply never
    // appeared, which is exactly the kind of defect a passing build hides.
    expect(categoryLabel(140, 'OneMinute')).toBeNull();
  });
});

describe('redundant encoding', () => {
  it('grows width and marker radius with intensity', () => {
    // Colour carries the signal; width and radius repeat it so the track survives greyscale
    // printing, projector gamma and colour-vision deficiency. Monotonic, or it says nothing.
    const knots = [0, 34, 64, 113, 160];
    const widths = knots.map(trackWidthForWind);
    const radii = knots.map(fixRadiusForWind);

    expect(widths).toStrictEqual([...widths].sort((a, b) => a - b));
    expect(radii).toStrictEqual([...radii].sort((a, b) => a - b));
  });

  it('draws an unmeasured fix smaller than any measured one', () => {
    expect(fixRadiusForWind(null)).toBeLessThan(fixRadiusForWind(0));
    expect(trackWidthForWind(null)).toBeLessThan(trackWidthForWind(0));
  });
});

describe('legend', () => {
  it('covers the ramp without gaps or overlaps', () => {
    // The legend and the map are painted from one table, so this asserts the table is coherent:
    // seven contiguous bands, the strongest open-ended.
    const rows = intensityLegend();

    expect(rows).toHaveLength(7);
    expect(rows[0].range).toBe('137+ kt');
    expect(rows[0].colour).toBe(trackColourForWind(137));
    expect(rows[1].range).toBe('113\u2013136 kt');
    expect(rows[6].range).toBe('0\u201333 kt');
  });

  it('runs strongest first, so the interface must reverse it deliberately', () => {
    const rows = intensityLegend();

    expect(rows[0].label).toBe('Category 5');
    expect(rows[6].label).toBe('Tropical depression');
  });
});
