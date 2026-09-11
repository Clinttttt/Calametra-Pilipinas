/**
 * CYCLONE INTENSITY COLOUR RAMP
 *
 * ── Why cyclones now carry hue ──────────────────────────────────────────────
 * The first version of this map reserved hue entirely for earthquake depth and drew every
 * cyclone track as a white line whose *width* carried intensity. That was defensible in
 * principle and a failure in practice: at national zoom the width difference between a
 * tropical depression (1.5 px) and a Category 5 (6 px) is roughly three pixels of white on a
 * pale satellite basemap, and four agency tracks separated only by opacity read as a single
 * scribble. Intensity was encoded but not perceivable, which is the same as not encoded.
 *
 * Every authoritative tropical-cyclone product — the NHC's best-track charts, JTWC's archive
 * graphics, IBTrACS' own plots, and the derived interactive maps a reader is most likely to
 * have seen — uses the Saffir–Simpson colour sequence below. Adopting it is not decoration:
 * it means a reader who has ever looked at a storm track already knows how to read this one
 * without consulting a legend. That is worth more than protecting hue for depth, especially
 * since a cyclone track and an earthquake circle are different geometries and are never
 * compared to one another.
 *
 * ── What the colour does and does not claim ─────────────────────────────────
 * The ramp is keyed to **reported wind speed in knots**, not to a category. The distinction
 * matters here more than it does in most products: the Saffir–Simpson scale is *defined* on a
 * one-minute sustained wind, and only JTWC reports that. JMA, KMA and HKO report ten-minute
 * winds, over which the same storm yields a lower number — for Yamaneko the one-minute reading
 * was actually the lower one, so no conversion factor reconciles them.
 *
 * Colouring a 115 kt ten-minute reading identically to a 115 kt one-minute reading therefore
 * states only "these two agencies reported the same number", which is true. It deliberately
 * does not state "these are the same category", which is not knowable. `categoryLabel`
 * returns a label only for one-minute tracks; for every other averaging period it returns
 * null and the interface shows the wind speed alone.
 */

/** A step in the ramp: the lower bound of a wind band and the colour it takes. */
interface IntensityStep {
  readonly minimumKnots: number;
  readonly colour: string;
  /** The Saffir–Simpson name, valid only for a one-minute sustained wind. */
  readonly oneMinuteLabel: string;
}

/**
 * The Saffir–Simpson sequence, ordered strongest first so a lookup returns on its first match.
 *
 * These are the colours used by the tropical-cyclone track maps in general circulation, and
 * they are chosen for a dark ground: the sequence runs cool blue through cyan and pale yellow
 * to saturated orange and red, so intensity reads as a temperature ramp even in greyscale.
 */
const INTENSITY_STEPS: readonly IntensityStep[] = [
  { minimumKnots: 137, colour: '#ff6060', oneMinuteLabel: 'Category 5' },
  { minimumKnots: 113, colour: '#ff8f20', oneMinuteLabel: 'Category 4' },
  { minimumKnots: 96, colour: '#ffc140', oneMinuteLabel: 'Category 3' },
  { minimumKnots: 83, colour: '#ffe775', oneMinuteLabel: 'Category 2' },
  { minimumKnots: 64, colour: '#ffffcc', oneMinuteLabel: 'Category 1' },
  { minimumKnots: 34, colour: '#00faf4', oneMinuteLabel: 'Tropical storm' },
  { minimumKnots: 0, colour: '#5ebaff', oneMinuteLabel: 'Tropical depression' },
];

/**
 * The colour for a fix whose agency reported pressure but no wind.
 *
 * Deliberately outside the ramp and desaturated. A missing wind reading is not a weak storm,
 * and giving it the tropical-depression blue would assert an intensity nobody measured.
 */
export const UNMEASURED_WIND_COLOUR = '#8b949e';

/** The colour for a reported sustained wind, or the unmeasured grey when there is none. */
export function trackColourForWind(knots: number | null): string {
  if (knots === null) {
    return UNMEASURED_WIND_COLOUR;
  }

  return INTENSITY_STEPS.find((step) => knots >= step.minimumKnots)?.colour ?? UNMEASURED_WIND_COLOUR;
}

/**
 * The wire value that denotes a one-minute sustained wind.
 *
 * The API sends a display string — `1-min`, `2-min`, `10-min` — not the enum name. Verified
 * against `/api/cyclones/{id}` for SURIGAE 2021, which returns five agencies reading `1-min`
 * (JTWC), `2-min` (CMA) and `10-min` (JMA, KMA, HKO). Comparing against `OneMinute` here, as an
 * earlier version did, silently suppressed the category for every agency including the one
 * entitled to it — a failure that produces no error, only a missing label.
 */
const ONE_MINUTE = '1-min';

/**
 * The Saffir–Simpson label, or null when the averaging period makes it meaningless.
 *
 * Only a one-minute sustained wind admits a category. For the ten-minute agencies the same knot
 * value describes a stronger storm, so applying the scale to it would overstate the reading.
 */
export function categoryLabel(knots: number | null, averagingPeriod: string): string | null {
  if (knots === null || averagingPeriod !== ONE_MINUTE) {
    return null;
  }

  return INTENSITY_STEPS.find((step) => knots >= step.minimumKnots)?.oneMinuteLabel ?? null;
}

/**
 * Line width in pixels for a sustained wind speed.
 *
 * Retained alongside colour rather than replaced by it. Width is redundant encoding, which is
 * the point: it survives greyscale printing, projector gamma and colour-vision deficiency, all
 * three of which a thesis defence can involve. The range is narrower than before — colour now
 * carries the signal, so width only needs to reinforce it.
 */
export function trackWidthForWind(knots: number | null): number {
  if (knots === null) {
    return 1.6;
  }

  if (knots >= 113) {
    return 3.6;
  }

  if (knots >= 64) {
    return 3;
  }

  return knots >= 34 ? 2.4 : 1.8;
}

/** Marker radius in pixels for a fix. Sized with the track so the two never look mismatched. */
export function fixRadiusForWind(knots: number | null): number {
  if (knots === null) {
    return 2.2;
  }

  if (knots >= 113) {
    return 4;
  }

  if (knots >= 64) {
    return 3.4;
  }

  return knots >= 34 ? 2.9 : 2.4;
}

/** The ordered legend rows, strongest first, as the interface should present them. */
export interface IntensityLegendRow {
  readonly label: string;
  readonly colour: string;
  readonly range: string;
}

/** Legend rows built from the same table the map paints from, so the two cannot drift. */
export function intensityLegend(): readonly IntensityLegendRow[] {
  return INTENSITY_STEPS.map((step, index) => {
    const above = index === 0 ? null : INTENSITY_STEPS[index - 1];

    return {
      label: step.oneMinuteLabel,
      colour: step.colour,
      range:
        above === null
          ? `${step.minimumKnots}+ kt`
          : `${step.minimumKnots}\u2013${above.minimumKnots - 1} kt`,
    };
  });
}
