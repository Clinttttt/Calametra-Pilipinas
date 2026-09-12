/**
 * HIGHLIGHT MARK
 *
 * The mark placed on the one thing the reader asked for — an earthquake located from the place
 * panel, a match chosen from a list.
 *
 * White, because in this design system colour is a data channel: hue belongs to depth and size to
 * magnitude, and the single accent is reserved for interaction. A saturated highlight would read as
 * a third data channel, and the amber has to keep meaning "there is a caveat about this" and nothing
 * else.
 *
 * Drawn as a ring around the marker rather than over it, so the event keeps its own depth colour and
 * magnitude size. The highlight says *which one*; it must not restate or overwrite what the marker
 * already encodes.
 */
export const HIGHLIGHT_COLOUR = '#ffffff';
