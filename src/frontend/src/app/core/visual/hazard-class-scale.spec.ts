import { placeOnScale } from './hazard-class-scale';

/**
 * Placing a published class on its own scale.
 *
 * The tests that matter here are the refusals. Merging PHIVOLCS's two liquefaction vocabularies into
 * one scale, or nearest-matching an unfamiliar class onto a step that looks similar, would both
 * produce a confident diagram of something the agency never said.
 */
describe('hazard class scale', () => {
  it('places a susceptibility class on the four-step susceptibility scale', () => {
    const placement = placeOnScale('Liquefaction potential', 'Least Susceptible');

    expect(placement?.scale.steps).toHaveLength(4);
    expect(placement?.stepIndex).toBe(0);
    expect(placement?.meaning).toContain('not expected to lose strength');
  });

  it('places a potential class on the three-step potential scale, not the susceptibility one', () => {
    const placement = placeOnScale('Liquefaction potential', 'High Potential');

    // Different studies mapped different areas in different vocabularies. Merging them would invent
    // an equivalence PHIVOLCS does not state — "High Potential" is the top of three, not of four.
    expect(placement?.scale.name).toBe('Liquefaction potential');
    expect(placement?.scale.steps).toHaveLength(3);
    expect(placement?.stepIndex).toBe(2);
  });

  it('draws PEIS with all ten steps although the layer publishes only three', () => {
    const placement = placeOnScale('Intensity (PEIS)', 'VIII');

    // A scale drawn with only the published steps would put VIII at the top and imply nothing worse
    // exists. PEIS runs to X.
    expect(placement?.scale.steps).toHaveLength(10);
    expect(placement?.stepIndex).toBe(7);
    expect(placement?.meaning).toContain('devastating');
  });

  it('tolerates the same agency writing a class in different cases', () => {
    expect(placeOnScale('Susceptibility', 'VERY HIGH')?.stepIndex).toBe(3);
    expect(placeOnScale('Susceptibility', 'Very High')?.stepIndex).toBe(3);
  });

  it('keeps two agencies apart under the one Susceptibility label', () => {
    // Read from each service's own renderer on 2026-09-12: PHIVOLCS publishes three
    // earthquake-induced classes worded "Low Susceptibility", MGB four rainfall classes worded "Low".
    // The wording is what distinguishes them.
    const phivolcs = placeOnScale('Susceptibility', 'Low Susceptibility');
    const mgb = placeOnScale('Susceptibility', 'Low');

    expect(phivolcs?.scale.steps).toHaveLength(3);
    expect(phivolcs?.meaning).toContain('earthquake shaking');
    expect(mgb?.scale.steps).toHaveLength(4);
    expect(mgb?.meaning).toContain('ordinary rainfall');
  });

  it('leaves the depositional zone off the landslide ladder', () => {
    // PHIVOLCS's fourth published class names where debris comes to rest, not how prone the ground is
    // — a process rather than an intensity, so it has no rung.
    expect(placeOnScale('Susceptibility', 'Depositional Zone')).toBeNull();
  });

  it('leaves an unrecognised class unplaced rather than guessing at a step', () => {
    // MGB's landslide legend also carries a debris-flow class, which is not a rung on the ordinal
    // ladder — it describes a process, not an intensity, so it belongs off the scale.
    expect(placeOnScale('Susceptibility', 'Debris flow path')).toBeNull();
    expect(placeOnScale('Intensity (PEIS)', 'XII')).toBeNull();
  });

  it('places nothing for an attribute that is not a classification', () => {
    expect(placeOnScale('Province', 'Metro Manila')).toBeNull();
    expect(placeOnScale('Year mapped', '2013')).toBeNull();
  });
});
