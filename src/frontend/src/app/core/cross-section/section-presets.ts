import type { SectionEndpoint } from './cross-section-store';

/**
 * A pre-drawn cross-section across a known subduction system.
 */
export interface SectionPreset {
  readonly id: string;
  /** The structure being cut, as a geologist would name it. */
  readonly name: string;
  /** Where in the country, for someone who does not know the trench names. */
  readonly region: string;
  /** What the profile shows, in one line. */
  readonly summary: string;
  readonly start: SectionEndpoint;
  readonly end: SectionEndpoint;
  readonly corridorKm: number;
}

/**
 * VERIFIED SECTION PRESETS
 *
 * Drawing a good cross-section requires knowing where the trenches are and which way
 * they dip, which is exactly the knowledge a first-time visitor lacks. Left to guess,
 * the natural instinct is to draw along the line of earthquakes rather than across it —
 * and a section drawn along-strike produces flat horizontal bands that reveal nothing,
 * making the whole tool look broken when it is working correctly.
 *
 * ── Every preset below was measured against the live archive ────────────────
 * Fifteen candidate lines were probed. Four survive. A candidate is kept only if the
 * seismicity deepens along it AND is distributed along its whole length — an empty
 * stretch renders as a blank third of the plot, which reads as a bug.
 *
 * Kept. Mean hypocentre depth, first quarter → last quarter, ±60 km corridor:
 *
 *   Manila Trench – Zambales      41 →  85 km    (+44)   309 pts, to 300 km
 *   Philippine Trench – Surigao   42 → 106 km    (+64)  1237 pts, to 650 km
 *   Negros Trench – Panay         46 → 172 km   (+126)   219 pts, to 650 km
 *   Cotabato Trench – Mindanao    35 → 268 km   (+233)   402 pts, to 700 km
 *
 * Rejected, recorded so they are not tried again:
 *
 *   Philippine Trench – Samar     12.0°N   −2 km across 471 pts. Flat.
 *   Philippine Trench – Leyte     10.8°N  +23 km. Flat.
 *   Philippine Trench – Bicol     13.5°N  +20 km. Flat.
 *   Philippine Trench – Davao      7.0°N  +26 km over 1076 pts. Flat.
 *   Manila Trench – Ilocos        17.5°N  empty first quarter (0 pts); trimmed to where
 *                                         events begin it dips only +10 km.
 *   Manila Trench – Mindoro       13.3°N  first quarter 2 pts; trimmed it runs the wrong
 *                                         way, 58 → 46 km.
 *   East Luzon Trough             16.0°N  first half nearly empty; trimmed, +24 km.
 *   Sulu Trench – Palawan          7.8°N  37 events. Too sparse to plot.
 *
 * ── Why only four ──────────────────────────────────────────────────────────
 * This is a property of the catalogue, not a shortage of trenches. The USGS archive holds
 * effectively nothing below M4.0 anywhere in the Philippines, so only the most active
 * slabs accumulate enough events *along* a line to form a profile. The rejected sections
 * cross real, well-mapped subduction zones; they are simply under-sampled at M4+. A fifth
 * preset becomes justifiable the moment a denser catalogue is ingested — PHIVOLCS records
 * M2–3 routinely — and adding one is a single entry in the array below.
 *
 * ── Orientation ────────────────────────────────────────────────────────────
 * Each line is oriented so the section reads shallow on the left and deep on the right,
 * following the slab downward as the eye travels. That means the Philippine Trench
 * profile runs east-to-west while the other three run west-to-east — the Philippine
 * Trench subducts westward beneath the archipelago, whereas Manila, Negros and Cotabato
 * subduct eastward. The direction of each line is therefore a statement about the
 * tectonics, not an arbitrary choice, and reversing one would invert its plot.
 */
export const SECTION_PRESETS: readonly SectionPreset[] = [
  {
    id: 'philippine-trench-surigao',
    name: 'Philippine Trench',
    region: 'Surigao · east Mindanao',
    summary: 'Subducts westward to 650 km. The deepest, densest slab in the archive.',
    start: { latitude: 9.9, longitude: 127.5 },
    end: { latitude: 9.9, longitude: 124.5 },
    corridorKm: 60,
  },
  {
    id: 'cotabato-trench',
    name: 'Cotabato Trench',
    region: 'west Mindanao · Moro Gulf',
    summary: 'The steepest gradient available: 35 km to 268 km across 331 km of section.',
    start: { latitude: 7.0, longitude: 121.5 },
    end: { latitude: 7.0, longitude: 124.5 },
    corridorKm: 60,
  },
  {
    id: 'negros-trench',
    name: 'Negros Trench',
    region: 'Panay · Negros',
    summary: 'Eastward-dipping slab beneath the Visayas, reaching 650 km.',
    start: { latitude: 9.8, longitude: 121.5 },
    end: { latitude: 9.8, longitude: 124.2 },
    corridorKm: 60,
  },
  {
    id: 'manila-trench-zambales',
    name: 'Manila Trench',
    region: 'Zambales · west Luzon',
    summary: 'Shallower and sparser — a young slab, and a contrast to Mindanao.',
    start: { latitude: 15.5, longitude: 118.5 },
    end: { latitude: 15.5, longitude: 121.5 },
    corridorKm: 60,
  },
];
