/**
 * CALAMETRA ICON SET
 *
 * Hand-authored glyphs. No icon library is used, for two reasons.
 *
 * First, identity: stock icon sets are recognisable, and an interface built
 * from them reads as assembled rather than designed.
 *
 * Second, and more importantly, vocabulary: this application needs glyphs that
 * no general-purpose set contains — a hypocentre below a surface line, a fault
 * trace with lateral offset, a depth cross-section, a seismogram trace. Drawing
 * them is the only way to name these concepts visually.
 *
 * ── DRAWING CONSTRAINTS ─────────────────────────────────────────────────────
 * Every glyph obeys the same rules, which is what makes a hand-drawn set look
 * like a set rather than a collection:
 *
 *   • 24 x 24 viewBox, with visual weight kept inside a 20 x 20 optical area
 *   • stroke-based, never filled, at 1.5 units
 *   • round caps and round joins
 *   • currentColor only, so colour is always the caller's decision
 *   • geometry on the half-pixel grid where possible, to stay crisp at 16px
 *
 * Two glyphs deliberately keep their conventional forms — search and play —
 * because their universal recognition is worth more than novelty.
 */

/** Every glyph available. A union type so a typo is a compile error. */
export type IconName =
  // Primary navigation
  | 'explore'
  | 'time'
  | 'events'
  | 'stories'
  | 'compare'
  | 'data-sources'
  // Seismic vocabulary — the glyphs no stock set has
  | 'seismogram'
  | 'epicentre'
  | 'depth-section'
  | 'fault-trace'
  | 'trench'
  // Lenses
  | 'lens-seismic'
  | 'lens-coastal'
  | 'lens-terrain'
  | 'lens-cyclone'
  | 'lens-exposure'
  | 'lens-history'
  // Map and camera
  | 'layers'
  | 'camera-top'
  | 'camera-tilt'
  | 'camera-terrain'
  | 'locate'
  | 'radius'
  // Controls
  | 'search'
  | 'hazards'
  | 'play'
  | 'pause'
  | 'step-back'
  | 'step-forward'
  | 'close'
  | 'chevron-right'
  | 'chevron-left'
  | 'chevron-down'
  | 'chevron-up'
  | 'expand'
  | 'collapse'
  | 'download'
  | 'legend'
  | 'filter'
  | 'info'
  | 'caution'
  // Interface theme
  | 'theme-dark'
  | 'theme-light'
  | 'theme-system';

/**
 * Path geometry per glyph. Multiple subpaths are separate strings so that
 * individual strokes stay readable and editable.
 */
export const ICON_PATHS: Record<IconName, readonly string[]> = {
  // ── PRIMARY NAVIGATION ──────────────────────────────────────────────────

  /**
   * Nested contour bands with a located point.
   *
   * Drawn from the Calametra mark, which renders the archipelago as topographic
   * contours. Using that language here ties the icon set to the identity without
   * copying the mark, and it says "terrain and location" more precisely than a
   * perspective grid did.
   */
  explore: [
    'M12 5.5c4 0 7 2.9 7 6.5s-3 6.5-7 6.5-7-2.9-7-6.5 3-6.5 7-6.5Z',
    'M12 8.75c2.2 0 3.9 1.5 3.9 3.25S14.2 15.25 12 15.25 8.1 13.75 8.1 12 9.8 8.75 12 8.75Z',
    'M12 12h.01',
  ],

  /** A time axis with a playhead: the Time Machine. */
  time: [
    'M3 15.5h18',
    'M6 12.5v3M10 13.5v2M18 12.5v3',
    'M14 15.5a1.75 1.75 0 1 1-3.5 0 1.75 1.75 0 0 1 3.5 0Z',
    'M12.25 8v5.75',
  ],

  /** A seismogram trace above a baseline: the event archive. */
  events: ['M2.5 12h3l2-5.5 3 11 2.5-7 1.5 3h7'],

  /** An open narrative spread with a marked passage: Story Mode. */
  stories: [
    'M12 6.5C10 5 7.5 4.5 4 5v13c3.5-.5 6 0 8 1.5 2-1.5 4.5-2 8-1.5V5c-3.5-.5-6 0-8 1.5Z',
    'M12 6.5v13',
    'M6.5 9.5h2.5M15 9.5h2.5',
  ],

  /** Two panes with a shared divider: split-screen comparison. */
  compare: ['M12 3.5v17', 'M4 6h5v12H4zM15 6h5v12h-5z'],

  /** A stack of records with an attribution mark: provenance. */
  'data-sources': [
    'M4 7.5c0-1.4 3.6-2.5 8-2.5s8 1.1 8 2.5S16.4 10 12 10 4 8.9 4 7.5Z',
    'M4 12c0 1.4 3.6 2.5 8 2.5',
    'M4 7.5v9c0 1.4 3.6 2.5 8 2.5',
    'M20 7.5v4',
    'M17.5 16.5h5M20 14v5',
  ],

  // ── SEISMIC VOCABULARY ──────────────────────────────────────────────────

  /** A recorded waveform with its drum baseline. */
  seismogram: ['M2.5 19h19', 'M2.5 11h2.5l1.5-5 2.5 9.5L11.5 8l2 6 1.5-3h6.5'],

  /**
   * Radiating arcs from a point.
   *
   * Matches the epicentre pulse in the Calametra mark — open arcs on one side
   * rather than closed rings, which reads as energy propagating outward instead
   * of as a static target.
   */
  epicentre: [
    'M12 12h.01',
    'M14.5 9.5a3.5 3.5 0 0 1 0 5',
    'M17 7a7 7 0 0 1 0 10',
    'M19.5 4.5a10.5 10.5 0 0 1 0 15',
    'M9.5 14.5a3.5 3.5 0 0 1 0-5',
  ],

  /**
   * A surface line with a hypocentre plotted beneath it. This is the glyph for
   * the cross-section, and the concept the whole depth feature rests on.
   */
  'depth-section': ['M2.5 6.5h19', 'M12 6.5v9', 'M13.75 16.5a1.75 1.75 0 1 1-3.5 0 1.75 1.75 0 0 1 3.5 0Z', 'M5.5 9.5h.01M18.5 12.5h.01'],

  /** A fault trace broken by lateral offset, with slip direction marked. */
  'fault-trace': ['M3 8.5h8', 'M13 15.5h8', 'M11 8.5l2 7', 'M6.5 5.5h3M14.5 18.5h3'],

  /** A subducting slab descending beneath a plate margin. */
  trench: ['M2.5 8h8l3 3', 'M13.5 11c2.5 2.5 4.5 5 5 8', 'M21.5 8h-4', 'M16 5.5l-2.5 2.5 2.5 2.5'],

  // ── LENSES ─────────────────────────────────────────────────────────────
  // Each lens glyph is its subject inside an implied aperture, so the set
  // reads as one family of "ways of looking".

  /** Seismic lens: waveform through an aperture. */
  'lens-seismic': ['M4 12h2.5l1.5-4 2 8 2-6 1.5 3H20', 'M12 3.5a8.5 8.5 0 1 0 0 17 8.5 8.5 0 0 0 0-17Z'],

  /** Coastal lens: shoreline swell. */
  'lens-coastal': ['M3 10.5c2-1.5 4-1.5 6 0s4 1.5 6 0 4-1.5 6 0', 'M3 15.5c2-1.5 4-1.5 6 0s4 1.5 6 0 4-1.5 6 0'],

  /**
   * Terrain lens: nested contour lines.
   *
   * Contours rather than mountain triangles, matching the mark's topographic
   * language. It is also the more accurate symbol — this platform shows elevation
   * as a surface, not as pictorial peaks.
   */
  'lens-terrain': [
    'M3 17.5c3-3.5 6-3.5 9 0s6 3.5 9 0',
    'M6 12.5c2-2.5 4-2.5 6 0s4 2.5 6 0',
    'M9 8c1.2-1.5 2.4-1.5 3.6 0',
  ],

  /** Cyclone lens: a scientific spiral, not a cartoon storm. */
  'lens-cyclone': ['M12 12c0-2.5 2-4.5 4.5-4.5S21 9.5 21 12', 'M12 12c0 2.5-2 4.5-4.5 4.5S3 14.5 3 12', 'M12 12c2 0 3.5-1.5 3.5-3.5', 'M12 12c-2 0-3.5 1.5-3.5 3.5'],

  /** Exposure lens: settlement footprint. */
  'lens-exposure': ['M3 19.5h18', 'M5.5 19.5v-7h4v7', 'M11.5 19.5V7h4v12.5', 'M17.5 19.5v-5h3v5'],

  /** History lens: stacked time strata with a marked horizon. */
  'lens-history': ['M3.5 7.5h17M3.5 12h17M3.5 16.5h17', 'M8 5.5v4M15 10v4M11 14.5v4'],

  // ── MAP AND CAMERA ─────────────────────────────────────────────────────

  layers: ['M12 3.5 3 8l9 4.5L21 8l-9-4.5Z', 'M3 12.5 12 17l9-4.5', 'M3 16.5 12 21l9-4.5'],

  /** Plan view: a square grid seen from directly above. */
  'camera-top': ['M4 4.5h16v15H4zM4 9.5h16M4 14.5h16M9 4.5v15M15 4.5v15'],

  /** Oblique view: the same grid in perspective. */
  'camera-tilt': ['M2 15.5 12 9l10 6.5-10 4-10-4Z', 'M7 12.5 17 18M17 12.5 7 18'],

  /** Terrain view: relief with an exaggerated vertical. */
  'camera-terrain': ['M2 18.5h20', 'M4 18.5 9 8l4 6 3-4 4 8.5Z'],

  /** A located position: crosshair over a point. */
  locate: ['M12 12h.01', 'M14.5 12a2.5 2.5 0 1 1-5 0 2.5 2.5 0 0 1 5 0Z', 'M12 2.5v3.5M12 18v3.5M2.5 12H6M18 12h3.5'],

  /** A search radius around a chosen place. */
  radius: ['M12 12h.01', 'M12 4a8 8 0 1 0 0 16 8 8 0 0 0 0-16Z', 'M12 12h8'],

  // ── CONTROLS ───────────────────────────────────────────────────────────
  // search and play keep their conventional forms: universal recognition
  // outweighs distinctiveness for the two most-used controls in any app.

  /** A magnifier whose lens carries a crosshair: search *on the map*. */
  search: ['M16 16l4.5 4.5', 'M10.5 4a6.5 6.5 0 1 0 0 13 6.5 6.5 0 0 0 0-13Z', 'M10.5 7.5v6M7.5 10.5h6'],

  play: ['M8 5.5v13l11-6.5-11-6.5Z'],

  pause: ['M8.5 5.5v13M15.5 5.5v13'],

  /** Step to the previous interval. */
  'step-back': ['M16.5 5.5v13L7 12l9.5-6.5Z', 'M5.5 5.5v13'],

  /** Step to the next interval. */
  'step-forward': ['M7.5 5.5v13L17 12 7.5 5.5Z', 'M18.5 5.5v13'],

  close: ['M6 6l12 12M18 6L6 18'],

  /**
   * Hazard selection. A diamond — the cartographic hazard-marker shape — with a single centre dot.
   * Distinct from `layers`, which switches published overlays on and off within a hazard, and from
   * the lens glyphs, which each name one hazard.
   *
   * The dot is one subpath. It was briefly two, at 10.5 and 12.5, which drew two marks a pixel
   * apart and read as a duplicated glyph.
   */
  hazards: ['M12 3.5 20.5 12 12 20.5 3.5 12 12 3.5Z', 'M12 11.75v.01'],

  'chevron-right': ['M9.5 5.5 16 12l-6.5 6.5'],

  /** Mirror of chevron-right. Present as its own path rather than a CSS rotation so that a
      back affordance is a distinct glyph in the set and cannot be confused with the
      expand/collapse corner marks, which is exactly what happened before. */
  'chevron-left': ['M14.5 5.5 8 12l6.5 6.5'],

  'chevron-down': ['M5.5 9.5 12 16l6.5-6.5'],

  'chevron-up': ['M5.5 14.5 12 8l6.5 6.5'],

  /** Enter presentation view: corner marks opening outward. */
  expand: ['M4 9.5V4h5.5M20 14.5V20h-5.5M14.5 4H20v5.5M9.5 20H4v-5.5'],

  /** Leave presentation view: corner marks closing inward. */
  collapse: ['M9.5 4v5.5H4M14.5 20v-5.5H20M20 9.5h-5.5V4M4 14.5h5.5V20'],

  /** Export the current view. */
  download: ['M12 4v10.5', 'M8 11l4 4 4-4', 'M4.5 19.5h15'],

  /** Legend / key. */
  legend: ['M4.5 7h3M4.5 12h3M4.5 17h3', 'M11 7h8.5M11 12h8.5M11 17h8.5'],

  // Three sliders rather than the conventional funnel. A funnel says "narrow this down"; these
  // controls set bounds on three named quantities — time, magnitude, depth — and the handles say
  // that the bounds are adjustable rather than a fixed sieve.
  filter: [
    'M4 7.5h6M14 7.5h6',
    'M4 12h10M18 12h2',
    'M4 16.5h3M11 16.5h9',
    'M12 5.5v4M16 10v4M9 14.5v4',
  ],

  info: ['M12 3.5a8.5 8.5 0 1 0 0 17 8.5 8.5 0 0 0 0-17Z', 'M12 11v5.5', 'M12 7.75h.01'],

  /**
   * Data-limitation notice. Keeps the conventional triangle so it is
   * understood immediately, but drawn with the same open stroke as the rest of
   * the set rather than as a filled warning sign — this marks a caveat to
   * read, never an alarm.
   */
  caution: ['M12 4.5 21 19.5H3L12 4.5Z', 'M12 10v4.25', 'M12 17h.01'],

  // ── INTERFACE THEME ────────────────────────────────────────────────────
  // Three glyphs sharing one circular footprint, so the segmented control reads as one
  // object with a moving state rather than three unrelated marks.

  /** A crescent. The conventional dark-mode mark, and its recognition is worth more here
   *  than novelty — the same reasoning that keeps search and play conventional. */
  'theme-dark': ['M20 14.5A8.5 8.5 0 0 1 9.5 4a8.5 8.5 0 1 0 10.5 10.5Z'],

  /** A disc with rays. Drawn on the same circle as the crescent so the two align exactly
   *  when the control switches between them. */
  'theme-light': [
    'M15.5 12a3.5 3.5 0 1 1-7 0 3.5 3.5 0 0 1 7 0Z',
    'M12 3.5v2M12 18.5v2M3.5 12h2M18.5 12h2',
    'M6 6l1.5 1.5M16.5 16.5 18 18M18 6l-1.5 1.5M7.5 16.5 6 18',
  ],

  /** A display outline: the machine decides, so the mark is the machine rather than a
   *  half-lit sun that would read as a third brightness. */
  'theme-system': ['M3.5 5.5h17v10h-17v-10Z', 'M9 19.5h6', 'M12 15.5v4'],
};

/** Compile-time guarantee that the union and the registry cannot drift apart. */
export const ICON_NAMES = Object.keys(ICON_PATHS) as readonly IconName[];
