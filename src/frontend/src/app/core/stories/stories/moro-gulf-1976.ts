import type { Story } from '../story-model';

/**
 * The 1976 Moro Gulf earthquake.
 *
 * Chosen because it is the clearest case in the archive of a flagship event whose depth is a
 * convention rather than a measurement, and whose magnitude differs between published accounts.
 *
 * Verified against the archive on 2026-09-10 via `/api/earthquakes/map`:
 *
 *   agency id usp0000hvb  (USGS ComCat)
 *   date      1976-08-16
 *   magnitude Ms 7.9
 *   depth     33 km, quality 2 — agency-assigned, not measured
 *   epicentre 6.262 N, 124.023 E
 *
 * The depth quality is the reason this story exists. `q=2` is the archive's flag for a depth the
 * agency assigned rather than resolved, and 33 km is the historical NEIC "normal depth" default.
 *
 * External figures, each cited in `sources` and none taken from memory:
 *
 *   NOAA NCEI     16 August 1976, 16:11:07 UTC, Mw 8.0
 *   GJI (1980)    Ms 7.8, seismic moment 1.9 x 10^28 dyne-cm
 *   Nat. Museum   maximum runup about 9 m at Lebak, Sultan Kudarat; epicentre ~96 km off Cotabato
 *
 * 16:11 UTC on 16 August is 00:11 on 17 August in Philippine time, which is why accounts differ on
 * the date as well as on the magnitude. The prose says so rather than picking one silently.
 */
const MORO_GULF_USGS_ID = 'usp0000hvb';

export const MORO_GULF_1976: Story = {
  id: 'moro-gulf-1976',
  title: 'A depth nobody measured',
  standfirst:
    'The deadliest earthquake in Philippine history is recorded in this archive at a depth of '
    + '33 kilometres. That number was never measured. It is a default, and it sits on a fifth of '
    + 'the catalogue.',
  place: 'Moro Gulf, Mindanao',
  when: '16 August 1976',
  hazard: 'earthquake',
  shows: 'Assigned depths, and how published magnitudes for one event disagree.',
  beats: [
    {
      id: 'arrival',
      heading: 'Ten past midnight, or ten past four in the afternoon',
      body: [
        'At 16:11 UTC on 16 August 1976 the sea floor beneath the Moro Gulf ruptured. In '
        + 'Philippine time that was 00:11 on the 17th, which is why published accounts of this '
        + 'earthquake do not agree on what day it happened.',
        'The archive stores the instant, not the calendar date, and renders it in the reader\u2019s '
        + 'terms. Two accounts that appear to describe different days are describing the same '
        + 'second.',
      ],
      camera: { latitude: 6.4, longitude: 124.0, zoom: 7.2 },
      highlights: [{ latitude: 6.262, longitude: 124.023, label: 'Epicentre' }],
    },
    {
      id: 'how-large',
      heading: 'How large was it?',
      body: [
        'This archive holds a surface-wave magnitude of 7.9, from the USGS catalogue. A 1980 paper '
        + 'in Geophysical Journal International puts the surface-wave magnitude at 7.8 and reports '
        + 'a seismic moment of 1.9 \u00d7 10\u00b2\u2078 dyne-cm. NOAA\u2019s record of the event '
        + 'lists a moment magnitude of 8.0. The National Museum of the Philippines describes it as '
        + 'magnitude 7.9.',
        'These are not four attempts at one number that three of them got wrong. Surface-wave and '
        + 'moment magnitude are different quantities, and moment magnitude estimates for events of '
        + 'this age are revised as methods improve. The spread is what the record actually contains.',
        'Calametra stores the reading it can attribute and names the agency beside it. It does not '
        + 'publish a consensus magnitude, because no such measurement exists.',
      ],
      camera: { latitude: 6.3, longitude: 124.02, zoom: 8.6 },
      focusEarthquakeAgencyId: MORO_GULF_USGS_ID,
      highlights: [{ latitude: 6.262, longitude: 124.023, label: 'Epicentre' }],
    },
    {
      id: 'the-depth',
      heading: 'Exactly thirty-three kilometres',
      body: [
        'The depth in this archive is 33 kilometres, and the archive flags it as assigned rather '
        + 'than measured. 33 km is the historical default the NEIC used when depth could not be '
        + 'resolved from the available recordings. Other accounts of this earthquake give 20 km.',
        'The figure is not wrong, and it is not a mistake in the catalogue. It is a placeholder '
        + 'that has the same shape as a measurement, which is what makes it dangerous. Plot depth '
        + 'naively across this archive and 5,581 events line up at exactly 33 km, drawing a flat '
        + 'band through the data that no geology produced.',
        'So the platform treats an assigned depth as a different kind of value from a measured one. '
        + 'It is marked wherever it appears, excluded from depth analysis by default, and the number '
        + 'set aside is always stated.',
      ],
      camera: { latitude: 6.262, longitude: 124.023, zoom: 9.4 },
      focusEarthquakeAgencyId: MORO_GULF_USGS_ID,
      highlights: [{ latitude: 6.262, longitude: 124.023, label: 'Depth assigned, not measured' }],
      caveat:
        'Across the whole archive 11,790 of 27,242 agency readings carry an assigned depth \u2014 '
        + '43%. The four recurring values are 33 km, 10 km, 35 km and 15 km.',
    },
    {
      id: 'the-trench',
      heading: 'A trench, and the events it has produced',
      body: [
        'The rupture is associated with the Cotabato Trench, the subduction boundary running along '
        + 'the western side of Mindanao beneath the Moro Gulf and the Celebes Sea. The same margin '
        + 'produced the 1918 Celebes Sea earthquake, which at magnitude 8.3 is the largest event in '
        + 'this archive.',
        'Two events above magnitude 7.9 on one margin within sixty years is the kind of pattern a '
        + 'catalogue is for. It is also the limit of what a catalogue can tell you: this archive '
        + 'holds where and how large, not how often such events recur.',
      ],
      camera: { latitude: 6.0, longitude: 124.4, zoom: 6.6, pitch: 0 },
      highlights: [
        { latitude: 6.262, longitude: 124.023, label: '1976' },
        { latitude: 5.538, longitude: 123.994, label: '1918' },
      ],
    },
    {
      id: 'the-water',
      heading: 'What the archive does not hold',
      body: [
        'The earthquake was followed by a tsunami along the coasts of the Moro Gulf and the '
        + 'northern Celebes Sea. The National Museum of the Philippines records a maximum wave '
        + 'height of about nine metres at Lebak in Sultan Kudarat, and it is the tsunami rather '
        + 'than the shaking that accounts for most of the loss of life.',
        'None of that is in this archive. Calametra holds catalogued earthquake parameters, storm '
        + 'tracks and fault geometry. It holds no runup measurements, no casualty figures and no '
        + 'damage assessments, so it does not narrate them \u2014 the sources panel points to the '
        + 'institutions that do.',
        'This matters for reading the map honestly. The largest circle in a region is the largest '
        + 'recorded magnitude there, not the greatest harm done.',
      ],
      camera: { latitude: 6.9, longitude: 124.1, zoom: 7.4 },
      highlights: [{ latitude: 6.262, longitude: 124.023, label: 'Epicentre' }],
      caveat:
        'Impact records for this event are held by the National Museum of the Philippines, NDRRMC '
        + 'and NOAA\u2019s tsunami database. Calametra does not reproduce them.',
    },
  ],
  sources: [
    {
      title: 'August 1976 Moro Gulf, Philippines',
      publisher: 'NOAA National Centers for Environmental Information',
      url: 'https://data.commerce.gov/august-1976-moro-gulf-philippines-images',
      note: 'Origin time 16 August 1976 16:11:07 UTC and moment magnitude 8.0.',
    },
    {
      title:
        '1976 August 16, Mindanao, Philippine earthquake (Ms = 7.8) \u2014 evidence for a '
        + 'subduction zone south of Mindanao',
      publisher: 'Geophysical Journal International',
      url: 'https://academic.oup.com/gji/article/57/1/51/716416',
      note: 'Surface-wave magnitude 7.8 and seismic moment 1.9 \u00d7 10\u00b2\u2078 dyne-cm.',
    },
    {
      title: 'Tsunami in the Philippines (World Tsunami Awareness Day)',
      publisher: 'National Museum of the Philippines',
      url:
        'https://www.nationalmuseum.gov.ph/2021/11/05/'
        + 'tsunami-in-the-philippines-world-tsunami-awareness-day/',
      note:
        'Maximum wave height about 9 m at Lebak, Sultan Kudarat; epicentre about 96 km off '
        + 'Cotabato; magnitude given as 7.9.',
    },
    {
      title: 'Earthquake catalogue (ComCat)',
      publisher: 'United States Geological Survey',
      url: 'https://earthquake.usgs.gov/earthquakes/search/',
      note:
        'The reading this archive stores: Ms 7.9 at an agency-assigned depth of 33 km. Ingested '
        + 'into Calametra and shown in the panel beside the map.',
    },
  ],
};
