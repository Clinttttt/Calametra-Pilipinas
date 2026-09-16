/**
 * Palette for the administrative boundary states.
 *
 * Separate from the hazard and depth ramps on purpose: a municipality outline is context, and giving it
 * a colour from an encoding ramp would make it read as a measurement. These are neutral, and the
 * selected state is the platform's own accent rather than any hazard's.
 */

/** The bulk outline. A cool grey that sits above the basemap without competing with the coastline. */
export const LGU_LINE_COLOUR = '#7c8794';

/** Hover fill. Barely there, because hover says "you could click this", not "this is the answer". */
export const LGU_HOVER_FILL = '#9fb0c0';

/** Selected fill. The accent, at low opacity so the archive underneath stays readable. */
export const LGU_SELECTED_FILL = '#4ea3c8';

/** Selected outline. The same accent at full strength, so the unit is unambiguous when zoomed out. */
export const LGU_SELECTED_LINE = '#7fd0ef';
