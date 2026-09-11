/**
 * CYCLONE CENTRE SYMBOL
 *
 * Draws the conventional meteorological tropical-cyclone glyph — a filled core with two trailing
 * spiral arms — as a raster image for MapLibre's `addImage`.
 *
 * ── Why a symbol rather than drawn geometry ─────────────────────────────────
 * The first attempt drew rotating arms as map geometry, scaled in kilometres. Two things were
 * wrong with it. Visually, the arms had to be large enough to see, which made them span hundreds
 * of kilometres and read as arbitrary sweeps across the sea rather than as a storm. Structurally,
 * regenerating that geometry on every animation frame put it in its own source, so it could be
 * written for a different fix than the centre marker and appeared to move ahead of it.
 *
 * A symbol solves both. `icon-rotate` turns the glyph without touching geometry, so the centre,
 * the wind bands and the symbol all live in one source and one `setData` call — they cannot
 * describe different moments. And a symbol is sized in screen pixels, so it never implies a
 * geographic extent it has not measured.
 *
 * ── Why this glyph ─────────────────────────────────────────────────────────
 * The core-and-two-arms form is the standard tropical-cyclone symbol on meteorological charts, so
 * it is recognisable rather than invented. Rotation is anticlockwise, which is the true sense of
 * circulation in the northern hemisphere where every storm in this archive occurs.
 */

import { trackColourForWind } from './cyclone-intensity';

/** Drawn at 4x the rendered size, so the glyph stays crisp on a high-density display. */
const SUPERSAMPLE = 4;

/**
 * The dark keyline drawn beneath the glyph.
 *
 * Necessary once the glyph is coloured. The centre sits on top of the wind field, which is now
 * also shaded by intensity, so a red glyph on a red core would vanish into it. A dark outline
 * separates the symbol from whatever is behind it — the same reason the track has a casing — and
 * it holds against the pale bathymetry basemap as well as the near-black vector ones.
 */
const KEYLINE = '#05070a';

/**
 * Builds the glyph as image data.
 *
 * Generated at runtime rather than shipped as a file: it is a handful of arcs, and a generated
 * icon cannot fall out of step with the colour tokens the way a baked PNG would. Drawing it twice
 * — keyline first, then the fill — is cheaper and more reliable than compositing a shadow.
 */
export function createCycloneSymbol(sizePx: number, colour: string): ImageData | null {
  const size = sizePx * SUPERSAMPLE;
  const canvas = document.createElement('canvas');

  canvas.width = size;
  canvas.height = size;

  const context = canvas.getContext('2d');

  if (context === null) {
    return null;
  }

  const centre = size / 2;
  const unit = size / 32;

  context.clearRect(0, 0, size, size);
  context.lineCap = 'round';
  context.lineJoin = 'round';

  // Two passes: the keyline underneath at a heavier weight, then the intensity colour on top.
  for (const pass of [
    { ink: KEYLINE, coreRadius: unit * 3.9, armWidth: unit * 3.6 },
    { ink: colour, coreRadius: unit * 3, armWidth: unit * 2.4 },
  ]) {
    context.strokeStyle = pass.ink;
    context.fillStyle = pass.ink;

    // The core: a filled disc at the centre of circulation.
    context.beginPath();
    context.arc(centre, centre, pass.coreRadius, 0, Math.PI * 2);
    context.fill();

    // Two arms, opposed, each sweeping outward and back. Anticlockwise, matching the northern
    // hemisphere. Drawn as arcs rather than a computed logarithmic spiral because at this size the
    // difference is invisible and an arc reads cleaner.
    context.lineWidth = pass.armWidth;

    for (const rotation of [0, Math.PI]) {
      context.save();
      context.translate(centre, centre);
      context.rotate(rotation);

      context.beginPath();
      // Starts at the core's edge, sweeps out to the rim, curling back on itself — the hook that
      // makes the glyph read as rotating rather than as a plus sign.
      context.arc(unit * 6, 0, unit * 6, Math.PI, Math.PI * 0.15, true);
      context.stroke();

      context.restore();
    }
  }

  return context.getImageData(0, 0, size, size);
}

/** The image id prefix. One image is registered per intensity band. */
const SYMBOL_IMAGE_PREFIX = 'calametra-cyclone-glyph';

/**
 * The intensity bands the glyph is generated for, with a representative wind speed for each.
 *
 * One image per band rather than one recolourable image, because MapLibre's `icon-color` applies
 * only to SDF icons and this glyph is a canvas raster — an SDF cannot carry the keyline, which is
 * what makes the coloured glyph legible over the coloured field. Eight images of a few kilobytes
 * each, generated once at style load, is the cheaper trade.
 *
 * The representative speeds are the band lower bounds from `cyclone-intensity.ts`, so the glyph
 * colour is produced by the same function as the track and the field and cannot drift from them.
 */
const SYMBOL_BANDS: readonly { readonly suffix: string; readonly knots: number | null }[] = [
  { suffix: 'unmeasured', knots: null },
  { suffix: 'td', knots: 0 },
  { suffix: 'ts', knots: 34 },
  { suffix: 'c1', knots: 64 },
  { suffix: 'c2', knots: 83 },
  { suffix: 'c3', knots: 96 },
  { suffix: 'c4', knots: 113 },
  { suffix: 'c5', knots: 137 },
];

/** Every glyph variant that must be registered, with the colour each is drawn in. */
export function cycloneSymbolVariants(): readonly { readonly id: string; readonly colour: string }[] {
  return SYMBOL_BANDS.map((band) => ({
    id: `${SYMBOL_IMAGE_PREFIX}-${band.suffix}`,
    colour: trackColourForWind(band.knots),
  }));
}

/**
 * The image id for a storm's sustained wind.
 *
 * Ordered strongest first so the first match wins, mirroring the ramp lookup. A null wind returns
 * the unmeasured variant rather than the weakest one: no reading is not a weak storm.
 */
export function cycloneSymbolImage(windKnots: number | null): string {
  if (windKnots === null) {
    return `${SYMBOL_IMAGE_PREFIX}-unmeasured`;
  }

  const band = [...SYMBOL_BANDS]
    .filter((candidate) => candidate.knots !== null)
    .reverse()
    .find((candidate) => windKnots >= (candidate.knots ?? 0));

  return `${SYMBOL_IMAGE_PREFIX}-${band?.suffix ?? 'td'}`;
}

/**
 * Screen size in pixels for a storm's symbol, from its sustained wind.
 *
 * Stepped rather than continuous, and tied to the recognised intensity classes: a tropical
 * depression, a tropical storm and a typhoon should be visibly different objects, whereas a
 * smooth ramp would make a 63-knot storm and a 64-knot typhoon indistinguishable at the moment
 * their classification changes.
 *
 * Screen pixels, deliberately. The symbol marks a position; it does not claim an extent. Extent is
 * the wind bands' job, and those are drawn in kilometres.
 */
export function cycloneSymbolSize(windKnots: number | null): number {
  if (windKnots === null) {
    return 22;
  }

  if (windKnots >= 130) {
    return 46;
  }

  if (windKnots >= 100) {
    return 40;
  }

  if (windKnots >= 64) {
    return 34;
  }

  return windKnots >= 34 ? 28 : 22;
}

/**
 * Whether an eyewall radius is worth drawing.
 *
 * Measured across the archive: systems below tropical-storm strength report a mean radius of
 * maximum wind of 44 nautical miles and a maximum of 200, while organised typhoons report a mean
 * of 17. A disorganised system has no eyewall — the large figure reflects a broad, ill-defined
 * centre rather than a ring of strongest winds — so drawing a 200 nautical mile circle and calling
 * it an eyewall would assert a structure the storm does not have.
 *
 * The value is still shown in the panel, where it is labelled and can be read for what it is. It
 * is the *drawing* that would mislead.
 */
export function eyewallIsMeaningful(windKnots: number | null, radiusNm: number | null): boolean {
  if (radiusNm === null || radiusNm <= 0) {
    return false;
  }

  // Tropical-storm strength or above, and a radius tight enough to describe a real eyewall.
  return windKnots !== null && windKnots >= 34 && radiusNm <= 90;
}
