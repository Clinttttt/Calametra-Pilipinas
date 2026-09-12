/**
 * ORDINAL HAZARD CLASSES
 *
 * A susceptibility rating is an *ordinal* quantity: Moderate is above Low and below High, and the
 * gaps between them are not measured. Printed as a word in a table it loses that — a reader sees
 * "Least Susceptible" and has no idea whether it is the bottom of four classes or the middle of
 * seven. Drawn as a position on the publisher's own scale, it says both at once.
 *
 * <b>Three rules this module exists to keep.</b>
 *
 * The scales are the publishers' own, transcribed from each service's legend, and they are kept
 * apart. PHIVOLCS publishes liquefaction in two vocabularies because different studies mapped
 * different areas — High/Moderate/Low *Potential* alongside Highly/Moderately/Generally/Least
 * *Susceptible* — and merging them into one four-step scale would invent an equivalence the agency
 * does not state. A record is placed on the scale its own words belong to, or on no scale at all.
 *
 * Nothing is ranked across hazards. A "High" liquefaction potential and PEIS VIII are not comparable
 * quantities, so each carries its own scale and no shared colour ramp.
 *
 * An unrecognised value is returned unplaced rather than guessed at. The alternative — nearest-match
 * on a string — would silently put a class the agency added tomorrow at whatever step happened to
 * look similar.
 */

/** One ordinal scale, coarsest step first. */
export interface OrdinalScale {
  /** What the scale measures, in the publisher's terms. */
  readonly name: string;

  /** The steps, lowest first. */
  readonly steps: readonly string[];

  /**
   * A short gloss on the step a reader is looking at, written from the publisher's own legend or
   * from the standard definition of the scale. Keyed by step.
   */
  readonly meanings: Readonly<Record<string, string>>;
}

/** Where a value sits on a scale, or null when the value is not on any scale we hold. */
export interface ScalePlacement {
  readonly scale: OrdinalScale;
  readonly stepIndex: number;
  readonly meaning: string | null;
}

/**
 * PHIVOLCS liquefaction, the susceptibility vocabulary.
 *
 * Four steps, from the service's own legend. Kept separate from the potential vocabulary below.
 */
const LIQUEFACTION_SUSCEPTIBILITY: OrdinalScale = {
  name: 'Liquefaction susceptibility',
  steps: ['Least Susceptible', 'Generally Susceptible', 'Moderately Susceptible', 'Highly Susceptible'],
  meanings: {
    'Least Susceptible':
      'Ground here is not expected to lose strength during shaking — typically firm or rocky, or too '
      + 'deep below the water table to saturate.',
    'Generally Susceptible':
      'Conditions for liquefaction exist but are not concentrated: loose ground and shallow water are '
      + 'present in places rather than throughout.',
    'Moderately Susceptible':
      'Loose saturated ground is common enough that strong shaking may cause sand to behave as a '
      + 'liquid in parts of this area.',
    'Highly Susceptible':
      'Loose, water-saturated ground — usually young river or coastal deposits. Strong shaking can '
      + 'make it behave as a liquid, sinking or tilting structures whose foundations were sound.',
  },
};

/** PHIVOLCS liquefaction, the potential vocabulary used where other studies mapped. */
const LIQUEFACTION_POTENTIAL: OrdinalScale = {
  name: 'Liquefaction potential',
  steps: ['Low Potential', 'Moderate Potential', 'High Potential'],
  meanings: {
    'Low Potential':
      'Liquefaction is not expected to be a significant hazard here under the shaking this mapping '
      + 'considered.',
    'Moderate Potential':
      'Liquefaction is possible where loose saturated deposits occur within this area.',
    'High Potential':
      'Loose, water-saturated ground likely to liquefy under strong shaking — young alluvial or '
      + 'reclaimed land, typically with a shallow water table.',
  },
};

/**
 * The Philippine Earthquake Intensity Scale, as PHIVOLCS publishes the hazard layer.
 *
 * Ten steps because PEIS has ten, even though this layer is published only at VI, VII and VIII: a
 * scale drawn with three steps would misrepresent where those three sit. Intensity is *shaking felt*,
 * not magnitude — one earthquake produces many intensities and one magnitude.
 */
const PEIS: OrdinalScale = {
  name: 'PHIVOLCS Earthquake Intensity Scale',
  steps: ['I', 'II', 'III', 'IV', 'V', 'VI', 'VII', 'VIII', 'IX', 'X'],
  meanings: {
    VI: 'Very strong. Many people are frightened and run outdoors; heavy objects topple and weak '
      + 'structures crack.',
    VII: 'Destructive. Most people find it difficult to stand; older or poorly built structures are '
      + 'damaged, and liquefaction and landslides may occur.',
    VIII: 'Very destructive to devastating. Structures of good construction are damaged, and '
      + 'liquefaction, lateral spreading and landsliding are widespread.',
    IX: 'Devastating. Most buildings are heavily damaged, and the ground cracks and fails extensively.',
    X: 'Completely devastating. Practically all structures are destroyed and the ground is grossly '
      + 'deformed.',
  },
};

/**
 * PHIVOLCS earthquake-induced landslide.
 *
 * Three steps, read from the service's own renderer (`eilclass`, codes 01-03) rather than assumed to
 * match MGB's four-step rainfall scale. The fourth published class, `Depositional Zone`, is
 * deliberately absent: it names where debris comes to rest rather than how prone the ground is, so it
 * is not a rung on this ladder and appears as a value with no scale.
 */
const EARTHQUAKE_LANDSLIDE: OrdinalScale = {
  name: 'Earthquake-induced landslide susceptibility',
  steps: ['Low Susceptibility', 'Moderate Susceptibility', 'High Susceptibility'],
  meanings: {
    'Low Susceptibility':
      'Slopes here are not expected to fail under earthquake shaking — typically gentle ground or '
      + 'competent rock.',
    'Moderate Susceptibility':
      'Slope failure is possible under strong shaking, in the steeper or weaker parts of this area.',
    'High Susceptibility':
      'Slopes prone to failing when shaken — steep, weathered or fractured ground. This is a different '
      + 'hazard from rainfall-triggered landsliding, and the two maps do not agree on which slopes '
      + 'matter.',
  },
};

/** DOST-MGB susceptibility, shared wording for the landslide and flood maps. */
const MGB_SUSCEPTIBILITY: OrdinalScale = {
  name: 'MGB susceptibility rating',
  steps: ['Low', 'Moderate', 'High', 'Very High'],
  meanings: {
    Low: 'Terrain, geology and slope here make failure or inundation unlikely under ordinary rainfall.',
    Moderate: 'Conditions that permit failure or flooding are present; heavy or prolonged rain is '
      + 'required.',
    High: 'Ground or drainage here is prone to failing under heavy rain.',
    'Very High': 'Among the most prone ground MGB maps — steep or weak slopes, or terrain where water '
      + 'concentrates.',
  },
};

/**
 * Attribute labels whose values may be placed on a scale, and the scales to try.
 *
 * Keyed on the label this platform renders rather than the publisher's raw field name, because those
 * are inconsistent between services from the same agency — ground shaking answers with spaced Title
 * Case aliases and earthquake-induced landslide with a bare column name.
 */
const SCALES_BY_LABEL: Readonly<Record<string, readonly OrdinalScale[]>> = {
  'Liquefaction potential': [LIQUEFACTION_SUSCEPTIBILITY, LIQUEFACTION_POTENTIAL],
  'Intensity (PEIS)': [PEIS],
  // Two agencies answer under this one label and their ladders differ: PHIVOLCS publishes three
  // earthquake-induced classes worded "Low Susceptibility", MGB four rainfall classes worded "Low".
  // Both are offered and exact matching keeps them apart — the wording is what distinguishes them, so
  // trimming it to "Low" would merge two agencies' scales.
  Susceptibility: [EARTHQUAKE_LANDSLIDE, MGB_SUSCEPTIBILITY],
};

/**
 * Places an attribute value on its publisher's scale.
 *
 * Matching is exact after trimming, and case-insensitive only because the same agency writes "Very
 * High" and "VERY HIGH" in different services. No fuzzy matching: an unknown class is unplaced, which
 * the interface renders as the value alone.
 */
export function placeOnScale(label: string, value: string): ScalePlacement | null {
  const scales = SCALES_BY_LABEL[label];

  if (!scales) {
    return null;
  }

  const needle = value.trim().toLowerCase();

  for (const scale of scales) {
    const stepIndex = scale.steps.findIndex((step) => step.toLowerCase() === needle);

    if (stepIndex >= 0) {
      return {
        scale,
        stepIndex,
        meaning: scale.meanings[scale.steps[stepIndex]] ?? null,
      };
    }
  }

  return null;
}
