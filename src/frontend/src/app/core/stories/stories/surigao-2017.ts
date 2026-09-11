import type { Story } from '../story-model';

/**
 * THE 2017 SURIGAO EARTHQUAKE
 *
 * The introductory story. Interfaces live in `../story-model.ts`; the rule this content follows —
 * prose supplies context, the archive supplies numbers — is documented there.
 */

/**
 * The 2017 Surigao earthquake.
 *
 * Chosen as the narrative anchor because it is the one event in the archive carrying
 * readings from two agencies, which makes it the only event where the platform's central
 * claim — that the same earthquake has more than one correct magnitude — can be shown
 * rather than asserted.
 *
 * Verified against the archive on 2026-09-07, and re-verified on 2026-09-10 after this story was
 * re-keyed onto agency identifiers:
 *
 *   SELECT s.agency, o.magnitude_scale, o.magnitude_value, o.depth_kilometres,
 *          o.depth_quality, o.observed_at, o.latitude, o.longitude
 *   FROM earthquake_observations o JOIN data_sources s ON s.id = o.data_source_id
 *   WHERE o.hazard_event_id = (
 *       SELECT hazard_event_id FROM earthquake_observations
 *       WHERE external_event_id = 'us20008ixa');
 *
 *   DOST-PHIVOLCS  Ms  6.7  10 km  Constrained        9.9300  125.4500
 *   USGS           Mww 6.5  15 km  OperatorAssigned   9.9071  125.4516
 *   observed_at    2017-02-10 14:03:43 UTC  =  22:03 PHT
 *
 *   epicentre separation  2.54 km   (ST_Distance between the two observations)
 *   nearest GEM traces    Offshore Surigao 10.9 km, Surigao Fault 13.6 km,
 *                         Leyte Fault Southernmost Segment 14.6 km
 *
 * The query is written against the agency identifier rather than an internal id so it can be re-run
 * against any rebuild of the archive. This event is the reason that matters most: it is the only one
 * here reachable by two agency identifiers, `us20008ixa` and the PHIVOLCS bulletin
 * `2017_0210_1403`, and both resolve to it.
 *
 * Archive-wide figures quoted in the prose, verified the same day:
 *
 *   27,242 observations across 27,241 events
 *   11,790 observations carry an assigned depth  (11,789 of them are an event's preferred
 *          reading — the one difference is this story's own USGS reading, which is assigned
 *          but not preferred, since PHIVOLCS is authoritative for Philippine events)
 *      173 observations use surface-wave magnitude
 *    3,633 events within 150 km of this epicentre, 3,612 incomparable with Ms 6.7
 */

/**
 * USGS ComCat's identifier for this earthquake.
 *
 * PHIVOLCS's own identifier for the same event is `2017_0210_1403` and resolves identically —
 * verified against `/api/earthquakes/external/`. USGS is used here because ComCat is the archive's
 * backbone and the id appears in the published USGS event page the sources cite.
 */
const SURIGAO_USGS_ID = 'us20008ixa';

export const SURIGAO_2017: Story = {
  id: 'surigao-2017',
  title: 'One earthquake, two answers',
  standfirst:
    'On a February evening in 2017, Surigao del Norte shook. Two agencies recorded it, and they '
    + 'do not agree on how large it was, how deep it was, or exactly where it happened. None of '
    + 'them is wrong.',
  place: 'Surigao del Norte',
  when: '10 February 2017',
  hazard: 'earthquake',
  shows: 'That one earthquake has more than one correct magnitude, depth and epicentre.',
  beats: [
    {
      id: 'arrival',
      heading: 'A few seconds past ten at night',
      body: [
        'At 22:03 on 10 February 2017 an earthquake struck offshore of Surigao del Norte, in '
        + 'north-eastern Mindanao. Instruments in the Philippines and abroad recorded the same '
        + 'ground motion within the same second.',
        'What each network concluded from those recordings is where this story starts.',
      ],
      camera: { latitude: 9.92, longitude: 125.45, zoom: 8.4 },
    },
    {
      id: 'two-magnitudes',
      heading: 'Two agencies, two magnitudes',
      body: [
        'DOST-PHIVOLCS recorded a surface-wave magnitude of 6.7. The United States Geological '
        + 'Survey recorded a moment magnitude of 6.5. Both readings are in the panel beside the '
        + 'map, each named with the agency that produced it.',
        'The instinct is to ask which is correct. The better question is what each number '
        + 'measures. Surface-wave and moment magnitude are different quantities derived from '
        + 'different parts of the seismogram. Subtracting one from the other produces a figure '
        + 'that describes nothing physical.',
        'Calametra therefore stores every agency reading separately and refuses to reduce an '
        + 'event to a single magnitude. Where you see a magnitude in this platform, you also see '
        + 'who reported it and on which scale.',
      ],
      camera: { latitude: 9.91, longitude: 125.45, zoom: 9.6 },
      focusEarthquakeAgencyId: SURIGAO_USGS_ID,
    },
    {
      id: 'two-epicentres',
      heading: 'And two epicentres',
      body: [
        'The disagreement is not only about size. PHIVOLCS placed the epicentre 2.54 km from '
        + 'where the USGS placed it.',
        'Locating an earthquake means solving for a position from arrival times at recording '
        + 'stations. A national network with instruments close to the source and a global network '
        + 'with distant instruments will arrive at slightly different answers, and both are '
        + 'honest results of their own data.',
        'The map shows the readings separately for this reason. A single averaged dot would be a '
        + 'position no agency reported.',
      ],
      camera: { latitude: 9.918, longitude: 125.451, zoom: 11.5 },
      focusEarthquakeAgencyId: SURIGAO_USGS_ID,
      highlights: [
        { latitude: 9.93, longitude: 125.45, label: 'PHIVOLCS' },
        { latitude: 9.9071, longitude: 125.4516, label: 'USGS' },
      ],
    },
    {
      id: 'depth',
      heading: 'One depth was measured. The other was assumed.',
      body: [
        'PHIVOLCS resolved the depth at 10 km. The USGS catalogue lists 15 km — but that value '
        + 'is one of a small set of defaults the catalogue uses when depth cannot be resolved '
        + 'from the recordings.',
        'This matters more than it first appears. Across the whole Philippine archive, 11,790 '
        + 'of the 27,242 agency readings carry a depth that was assigned rather than measured: '
        + '33 km, 10 km, 35 km or 15 km, over and over. That is 43% of the catalogue.',
        'So the two depths here cannot be differenced at all. Not because they are far apart, '
        + 'but because one of them is a convention rather than a measurement.',
      ],
      camera: { latitude: 9.918, longitude: 125.451, zoom: 10.5 },
      focusEarthquakeAgencyId: SURIGAO_USGS_ID,
      caveat:
        'Calametra marks assigned depths everywhere they appear, excludes them from depth '
        + 'analysis by default, and says how many it set aside.',
    },
    {
      id: 'which-fault',
      heading: 'Which fault moved?',
      body: [
        'Three mapped fault traces lie within 15 km of the epicentre: Offshore Surigao at '
        + '10.9 km, the Surigao Fault at 13.6 km, and the southernmost segment of the Leyte '
        + 'Fault at 14.6 km. The Philippine Fault system runs through this region and all three '
        + 'are part of it.',
        'The traces are now drawn on the map. Proximity is suggestive and nothing more — a fault '
        + 'near an earthquake is not evidence that it produced it. Establishing which structure '
        + 'ruptured requires fault-plane solutions and field investigation, and it is the '
        + 'responsible agency that makes that determination.',
      ],
      camera: { latitude: 9.93, longitude: 125.42, zoom: 9.8 },
      focusEarthquakeAgencyId: SURIGAO_USGS_ID,
      layerNames: ['Active Faults (GEM)'],
      caveat:
        'Fault traces here come from the GEM global compilation, which is openly licensed and '
        + 'therefore storable. PHIVOLCS publishes far more detailed traces; those are displayed '
        + 'as imagery only, pending a signed data agreement.',
    },
    {
      id: 'comparable',
      heading: 'Now try to find a similar earthquake',
      body: [
        'A reasonable next question is what else like this has happened nearby. The archive holds '
        + '3,633 earthquakes within 150 km of this epicentre.',
        'Of those, 3,612 report a magnitude that cannot be compared with a surface-wave 6.7. '
        + 'Surface-wave magnitude accounts for 173 of the 27,242 readings in the catalogue; the '
        + 'rest are overwhelmingly body-wave. Exactly one nearby event can be set against this '
        + 'one on equal terms.',
        'That is not a defect in the search. It is the shape of the available data, and a tool '
        + 'that returned ten confident matches would be concealing it.',
      ],
      camera: { latitude: 9.9, longitude: 125.5, zoom: 7.6 },
      focusEarthquakeAgencyId: SURIGAO_USGS_ID,
    },
    {
      id: 'close',
      heading: 'What the record can and cannot say',
      body: [
        'This event is comparatively well documented: two agencies, two independent solutions, a '
        + 'mapped fault system, and a measured depth from the national network.',
        'Even so, its magnitude cannot be stated as one number, its epicentre cannot be stated '
        + 'as one point, one of its two depths was never measured, and the fault that moved '
        + 'cannot be named from this data alone.',
        'Calametra exists to make those limits visible rather than to smooth them away. Every '
        + 'other event in the archive is described less completely than this one.',
      ],
      camera: { latitude: 12.5, longitude: 122.5, zoom: 5.2 },
    },
  ],
  sources: [
    {
      title: 'Earthquake bulletin, 10 February 2017',
      publisher: 'DOST-PHIVOLCS',
      url: 'https://www.phivolcs.dost.gov.ph/',
      note:
        'The Ms 6.7 reading at a constrained depth of 10 km, and the epicentre at 9.93 N 125.45 E. '
        + 'Transcribed from the published bulletin, since PHIVOLCS vector data cannot be stored '
        + 'without a signed agreement.',
    },
    {
      title: 'M 6.5 \u2014 Surigao, Philippines (us20008ixa)',
      publisher: 'United States Geological Survey',
      url: 'https://earthquake.usgs.gov/earthquakes/eventpage/us20008ixa',
      note: 'The Mww 6.5 reading at an operator-assigned depth of 15 km.',
    },
    {
      title: 'Global Active Faults database',
      publisher: 'GEM Foundation',
      url: 'https://github.com/GEMScienceTools/gem-global-active-faults',
      note:
        'The three fault traces within 15 km of the epicentre. CC BY-SA 4.0, which is why this '
        + 'platform can store and measure against them.',
    },
  ],
};
