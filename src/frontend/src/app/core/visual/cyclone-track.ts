import type { AgencyTrack, CycloneFix } from '../api/contracts';
import { fixRadiusForWind, trackColourForWind, trackWidthForWind } from './cyclone-intensity';

/**
 * CYCLONE TRACK GEOMETRY
 *
 * Turns the API's per-agency fix lists into the features the map paints. Three kinds of
 * feature come out of here, distinguished by a `role` property rather than by geometry type,
 * because two of them are points and a layer filter on `geometry-type` alone could not tell
 * them apart:
 *
 *   `segment`  one line per consecutive pair of fixes, carrying the colour and width of the
 *              intensity at the fix it leaves
 *   `fix`      one point per fix — the six-hourly observation marker
 *   `landfall` the coast crossing, for the emphasised agency only
 *
 * ── Why a marker at every fix ───────────────────────────────────────────────
 * A best-track is a sequence of discrete observations, normally six hours apart, and every
 * authoritative rendering of one shows those observations. Drawing only the interpolated line
 * hides the two things a reader most needs: how often the storm was actually observed, and
 * where it slowed down. Markers make both visible — they bunch where the storm stalled and
 * stretch where it raced, and a gap in them is a gap in the record rather than a straight fast
 * leg. On the Yamaneko track that difference is the difference between a scribble and a
 * measured path.
 *
 * ── Why segments rather than one line per agency ────────────────────────────
 * Intensity changes along a track and a single LineString can carry only one colour and one
 * width. A long storm across four agencies yields a few hundred segments, which costs nothing
 * measurable and lets the ramp run continuously along the path the way a best-track chart does.
 */

/** Properties shared by every feature, so a layer filter can always find what it needs. */
interface TrackFeatureProperties {
  readonly role: 'segment' | 'fix' | 'landfall';
  readonly sourceSlug: string;
  readonly emphasised: boolean;
}

interface TrackSegment {
  readonly type: 'Feature';
  readonly properties: TrackFeatureProperties & {
    readonly colour: string;
    readonly width: number;
    /** Kept on the feature so a hover readout does not need to re-derive it. */
    readonly windKnots: number | null;
  };
  readonly geometry: {
    readonly type: 'LineString';
    readonly coordinates: readonly [number, number][];
  };
}

interface TrackPoint {
  readonly type: 'Feature';
  readonly properties: TrackFeatureProperties & {
    readonly colour: string;
    readonly radius: number;
    readonly windKnots: number | null;
  };
  readonly geometry: { readonly type: 'Point'; readonly coordinates: readonly [number, number] };
}

export interface CycloneGeoJson {
  readonly type: 'FeatureCollection';
  readonly features: readonly (TrackSegment | TrackPoint)[];
}

/**
 * True when a pair of fixes straddles the antimeridian.
 *
 * Western-Pacific storms do cross it, and without this a track would draw a line straight back
 * across the whole map. The segment is dropped rather than split: the platform's viewport is the
 * Philippine archipelago, so the discarded leg is always far outside it.
 */
function crossesDateline(from: CycloneFix, to: CycloneFix): boolean {
  return Math.abs(to.longitude - from.longitude) > 180;
}

/** Builds the drawable geometry for every agency's track. */
export function toCycloneGeoJson(
  tracks: readonly AgencyTrack[],
  emphasisedSlug: string | null,
): CycloneGeoJson {
  const features: (TrackSegment | TrackPoint)[] = [];

  for (const track of tracks) {
    const emphasised = track.sourceSlug === emphasisedSlug;

    for (let i = 0; i < track.fixes.length - 1; i++) {
      const from = track.fixes[i];
      const to = track.fixes[i + 1];

      if (crossesDateline(from, to)) {
        continue;
      }

      features.push({
        type: 'Feature',
        properties: {
          role: 'segment',
          sourceSlug: track.sourceSlug,
          emphasised,
          // The segment takes the intensity of the fix it leaves, so a reading is attributed to
          // the interval it was measured in rather than to the one after it.
          colour: trackColourForWind(from.windKnots),
          width: trackWidthForWind(from.windKnots),
          windKnots: from.windKnots,
        },
        geometry: {
          type: 'LineString',
          coordinates: [
            [from.longitude, from.latitude],
            [to.longitude, to.latitude],
          ],
        },
      });
    }

    // Observation markers for the emphasised agency only. Four overlapping sets of six-hourly
    // markers would be denser than the tracks they annotate and would suggest four times as
    // many observations as any one agency made.
    if (emphasised) {
      for (const fix of track.fixes) {
        features.push({
          type: 'Feature',
          properties: {
            role: 'fix',
            sourceSlug: track.sourceSlug,
            emphasised,
            colour: trackColourForWind(fix.windKnots),
            radius: fixRadiusForWind(fix.windKnots),
            windKnots: fix.windKnots,
          },
          geometry: { type: 'Point', coordinates: [fix.longitude, fix.latitude] },
        });
      }
    }

    // Landfall for the emphasised agency only. Every agency flags it at a slightly different
    // fix, and showing all of them would read as several separate landfalls.
    if (emphasised) {
      for (const landfall of track.fixes.filter((fix) => fix.isLandfall)) {
        features.push({
          type: 'Feature',
          properties: {
            role: 'landfall',
            sourceSlug: track.sourceSlug,
            emphasised,
            colour: trackColourForWind(landfall.windKnots),
            radius: fixRadiusForWind(landfall.windKnots),
            windKnots: landfall.windKnots,
          },
          geometry: { type: 'Point', coordinates: [landfall.longitude, landfall.latitude] },
        });
      }
    }
  }

  return { type: 'FeatureCollection', features };
}

/** The moving centre position during playback. */
export function toCurrentPositionGeoJson(fix: CycloneFix | null): CycloneGeoJson {
  if (fix === null) {
    return { type: 'FeatureCollection', features: [] };
  }

  return {
    type: 'FeatureCollection',
    features: [
      {
        type: 'Feature',
        properties: {
          role: 'fix',
          sourceSlug: 'current',
          emphasised: true,
          colour: trackColourForWind(fix.windKnots),
          radius: fixRadiusForWind(fix.windKnots),
          windKnots: fix.windKnots,
        },
        geometry: { type: 'Point', coordinates: [fix.longitude, fix.latitude] },
      },
    ],
  };
}
