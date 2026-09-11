import { describe, expect, it } from 'vitest';

import {
  cycloneSymbolImage,
  cycloneSymbolSize,
  cycloneSymbolVariants,
  eyewallIsMeaningful,
} from './cyclone-symbol';
import { trackColourForWind } from './cyclone-intensity';

/**
 * The centre glyph's intensity variants, and the eyewall gate.
 *
 * The variant set is the failure-prone part: `icon-image` selects by string, so a mismatch between
 * the id a feature asks for and the ids registered with the map produces a missing-image warning
 * and — because this project installs a `styleimagemissing` handler that supplies a transparent
 * placeholder — an invisible glyph rather than an error. Nothing would fail loudly. So the mapping
 * is asserted against the registered set directly.
 */
describe('cyclone symbol variants', () => {
  it('registers one variant per band, coloured by the shared ramp', () => {
    const variants = cycloneSymbolVariants();

    expect(variants).toHaveLength(8);
    // Colours must come from the same function the track and field use, or the glyph would drift
    // away from the line it sits on.
    expect(variants.map((variant) => variant.colour)).toContain(trackColourForWind(137));
    expect(variants.map((variant) => variant.colour)).toContain(trackColourForWind(null));
  });

  it('only ever asks for an id that was registered', () => {
    // The check that matters. Every wind speed the archive can hold, including nulls and values
    // beyond the top band, must resolve to a registered image.
    const registered = new Set(cycloneSymbolVariants().map((variant) => variant.id));
    const speeds: readonly (number | null)[] = [null, 0, 20, 33, 34, 63, 64, 82, 96, 113, 137, 200];

    for (const knots of speeds) {
      expect(registered.has(cycloneSymbolImage(knots)), `${knots}`).toBe(true);
    }
  });

  it('selects the band matching the track colour for the same wind', () => {
    // Glyph and track must agree. Derive the expected variant from the ramp colour rather than
    // hard-coding the suffix, so this stays true if a band is ever added.
    const variants = cycloneSymbolVariants();

    for (const knots of [0, 34, 64, 83, 96, 113, 137, 190]) {
      const chosen = variants.find((variant) => variant.id === cycloneSymbolImage(knots));

      expect(chosen?.colour, `${knots} kt`).toBe(trackColourForWind(knots));
    }
  });

  it('gives an unmeasured wind its own variant, not the weakest one', () => {
    expect(cycloneSymbolImage(null)).not.toBe(cycloneSymbolImage(0));
  });
});

describe('symbol size', () => {
  it('grows with intensity class and never shrinks', () => {
    const sizes = [null, 0, 34, 64, 100, 130].map(cycloneSymbolSize);

    expect(sizes).toStrictEqual([...sizes].sort((a, b) => a - b));
  });
});

describe('eyewall gate', () => {
  it('refuses an eyewall for a disorganised system', () => {
    // Measured across the archive: sub-tropical-storm systems report a mean radius of maximum wind
    // of 44 nmi and a maximum of 200. That describes a broad, ill-defined centre, not a ring of
    // strongest winds, so drawing it would assert absent structure.
    expect(eyewallIsMeaningful(25, 60)).toBe(false);
    expect(eyewallIsMeaningful(120, 200)).toBe(false);
  });

  it('accepts a tight radius on an organised storm', () => {
    expect(eyewallIsMeaningful(120, 17)).toBe(true);
    expect(eyewallIsMeaningful(34, 90)).toBe(true);
  });

  it('refuses a missing or zero radius', () => {
    expect(eyewallIsMeaningful(120, null)).toBe(false);
    expect(eyewallIsMeaningful(120, 0)).toBe(false);
    expect(eyewallIsMeaningful(null, 17)).toBe(false);
  });
});
