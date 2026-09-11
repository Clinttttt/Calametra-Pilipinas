import type { Story } from '../story-model';

/**
 * The 2019 Cotabato \u2013 Davao del Sur sequence.
 *
 * Chosen because every other earthquake story in this catalogue is about one event, and the record's
 * unit of account is the event. This one is about what happens when the ground does not oblige: a
 * place experienced months of strong shaking, and the archive holds it as a list.
 *
 * It also carries a data-quality point the single-event stories cannot: inside a dense sequence the
 * proportion of never-measured depths becomes overwhelming, so the sequence's vertical structure \u2014
 * the thing a seismologist would most want \u2014 is largely absent.
 *
 * ── Verified against the archive on 2026-09-10 ──────────────────────────────
 *
 *   SELECT external_event_id, observed_at, magnitude_scale, magnitude_value,
 *          depth_kilometres, depth_quality
 *   FROM earthquake_observations
 *   WHERE observed_at >= '2019-10-01' AND observed_at < '2020-01-01'
 *     AND latitude BETWEEN 6.2 AND 7.4 AND longitude BETWEEN 124.5 AND 125.7
 *     AND magnitude_value >= 5.7 ORDER BY observed_at;
 *
 *   us70005ulm  2019-10-16 11:37Z  Mww 6.4  16.1 km  Constrained        19:37 PST
 *   us6000645n  2019-10-29 01:04Z  Mww 6.6  15.0 km  OperatorAssigned   09:04 PST
 *   us60006470  2019-10-29 02:42Z  Mww 5.9  10.0 km  OperatorAssigned   10:42 PST
 *   us700061e9  2019-10-31 01:11Z  Mww 6.5  10.0 km  OperatorAssigned   09:11 PST
 *   us60006rp9  2019-12-15 06:11Z  Mww 6.8  18.0 km  Constrained        14:11 PST
 *
 * Separations between the four M6.0+ events, by ST_Distance on the stored geography:
 *
 *   widest pair   28.7 km   (16 Oct \u2194 31 Oct)
 *   closest pair   4.6 km   (16 Oct \u2194 29 Oct)
 *
 * The whole cluster, 16 October to 31 December in the same box:
 *
 *   154 catalogued events
 *    29 at M5.0 and above
 *     4 at M6.0 and above
 *   121 of the 154 carry an agency-assigned depth  (78.6%)
 *
 * ── External figures, each cited in `sources` ───────────────────────────────
 *
 *   Natural Hazards      "a seismic sequence comprising four earthquakes with magnitude MW > 6.0
 *   (Ferrario et al.)    (EQ1-4; max magnitude MW 6.8)"; triggered landslides and liquefaction
 *   EGU abstract         "five M~6 (Mw 6.4, 6.6, 5.9, 6.5, and 6.7) inland earthquakes"
 *   PHIVOLCS (Oct 2019)  October events at depths of 7 to 9 km; 16 Oct at 7:37 PM PST
 *   Geomatics, NH&R      InSAR places the December mainshock on a single NW\u2013SE plane within the
 *                        Cotabato fault system, the Makilala\u2013Malungon fault being causative
 *
 * Note that the archive contains every event in the EGU list, including the Mww 5.9. The published
 * counts differ because the authors set different magnitude thresholds, not because their data
 * differ from this archive's. That is the story's central observation and is stated as such.
 *
 * The December magnitude is Mww 6.8 here and in the Natural Hazards paper, and 6.7 in the EGU
 * abstract. The depth disagreement is larger and more instructive: PHIVOLCS reports 7\u20139 km for the
 * October events where USGS reports 10\u201316 km, two of them assigned.
 */

/** The 16 October event: the sequence's first M6, and a measured depth. */
const OCT_16_USGS_ID = 'us70005ulm';

/** The 31 October event: an assigned depth of exactly 10 km. */
const OCT_31_USGS_ID = 'us700061e9';

/** The 15 December event: the largest, and the last. */
const DEC_15_USGS_ID = 'us60006rp9';

export const COTABATO_2019: Story = {
  id: 'cotabato-2019',
  title: 'Four earthquakes, or one?',
  standfirst:
    'Between October and December 2019 four earthquakes above magnitude 6 struck within 29 '
    + 'kilometres of each other in Cotabato and Davao del Sur. The largest arrived last. Published '
    + 'accounts cannot agree on how many there were.',
  place: 'Cotabato and Davao del Sur, Mindanao',
  when: '16 October to 15 December 2019',
  hazard: 'earthquake',
  shows: 'That the catalogue counts events, while a place experiences a sequence.',
  beats: [
    {
      id: 'first',
      heading: 'The sixteenth of October',
      body: [
        'At 7:37 in the evening, Philippine time, a magnitude 6.4 earthquake struck inland Cotabato. '
        + 'This archive holds it at 6.715 north, 125.007 east, at a measured depth of 16.1 '
        + 'kilometres.',
        'On its own it would be an ordinary entry in the record: one strong, shallow, inland event. '
        + 'It was the first of at least four.',
      ],
      camera: { latitude: 6.75, longitude: 125.05, zoom: 8.6 },
      focusEarthquakeAgencyId: OCT_16_USGS_ID,
      highlights: [{ latitude: 6.715, longitude: 125.007, label: '16 October, M6.4' }],
    },
    {
      id: 'thirteen-days',
      heading: 'Then thirteen days later, twice',
      body: [
        'On 29 October a magnitude 6.6 struck 4.6 kilometres from the first. Ninety-eight minutes '
        + 'after it, a 5.9. Two days later, on 31 October, a 6.5.',
        'Three of those four had by now arrived, all within five kilometres to twenty-nine '
        + 'kilometres of one another. For anyone living above them the distinction between one '
        + 'earthquake and several is academic: the ground had been shaking, on and off, for a '
        + 'fortnight.',
      ],
      camera: { latitude: 6.8, longitude: 125.09, zoom: 9.2 },
      focusEarthquakeAgencyId: OCT_31_USGS_ID,
      highlights: [
        { latitude: 6.757, longitude: 125.008, label: '29 October, M6.6' },
        { latitude: 6.91, longitude: 125.178, label: '31 October, M6.5' },
      ],
    },
    {
      id: 'largest-last',
      heading: 'The largest one came last',
      body: [
        'On 15 December, sixty days after the first, a magnitude 6.8 struck the same ground at a '
        + 'measured depth of 18 kilometres. It is the largest of the sequence.',
        'That ordering matters, because the vocabulary usually applied to earthquake sequences does '
        + 'not fit it. A mainshock followed by aftershocks implies the biggest event comes first and '
        + 'everything after is a decaying tail. Here the biggest came two months in. Which event is '
        + '"the" earthquake is a question the catalogue does not answer and cannot.',
      ],
      camera: { latitude: 6.7, longitude: 125.17, zoom: 9 },
      focusEarthquakeAgencyId: DEC_15_USGS_ID,
      highlights: [{ latitude: 6.697, longitude: 125.174, label: '15 December, M6.8' }],
    },
    {
      id: 'depths',
      heading: 'And most of the depths were never measured',
      body: [
        'Across the whole sequence \u2014 154 catalogued events in this box between 16 October and the '
        + 'end of the year \u2014 121 carry a depth the agency assigned rather than resolved. That is '
        + '78 per cent. Two of the four largest are among them: the 29 October M6.6 at exactly 15 '
        + 'kilometres, and the 31 October M6.5 at exactly 10 kilometres.',
        'So the question a seismologist would ask first \u2014 did the sequence migrate upward, '
        + 'downward, along strike? \u2014 cannot be asked of this data. Calametra will not difference '
        + 'the depth of the 29 October event against the 15 December one, because one of the two '
        + 'numbers is a convention and the subtraction would look like a measurement.',
      ],
      camera: { latitude: 6.8, longitude: 125.09, zoom: 8.4 },
      focusEarthquakeAgencyId: OCT_31_USGS_ID,
      caveat:
        '10 km, 15 km, 33 km and 35 km are the four assigned-depth conventions in this archive. '
        + 'Exact values are the detection: depthError is populated for these events, so filtering on '
        + 'error will not find them.',
    },
    {
      id: 'disagreement',
      heading: 'The published accounts disagree on the count',
      body: [
        'A study in Natural Hazards describes "a seismic sequence comprising four earthquakes with '
        + 'magnitude MW > 6.0" with a maximum of MW 6.8. A conference abstract on the same sequence '
        + 'describes five, listing 6.4, 6.6, 5.9, 6.5 and 6.7.',
        'Both are correct. This archive holds all five of those events; the difference is where each '
        + 'author set the threshold, and whether the December event is read as 6.7 or 6.8. The '
        + 'sequence did not change. The counting rule did.',
        'The depths diverge further. PHIVOLCS reported the October events at 7 to 9 kilometres; the '
        + 'USGS readings stored here are 10 to 16 kilometres, two of them assigned. A national '
        + 'network with stations close above the source and a global network solving from far away '
        + 'produce different answers, and neither is the error of the other.',
      ],
      camera: { latitude: 6.8, longitude: 125.09, zoom: 8 },
      focusEarthquakeAgencyId: DEC_15_USGS_ID,
    },
    {
      id: 'fault',
      heading: 'Which structure moved',
      body: [
        'Analysis of ground deformation measured from orbit places the December mainshock on a '
        + 'single northwest\u2013southeast plane within the Cotabato fault system, and identifies the '
        + 'Makilala\u2013Malungon fault as the causative structure.',
        'That is attribution earned by measurement, not by proximity. The fault traces now drawn on '
        + 'the map are the GEM global compilation, and this platform shows them as context: a line '
        + 'near an epicentre is a candidate, never a conclusion.',
      ],
      camera: { latitude: 6.75, longitude: 125.0, zoom: 8.2 },
      layerNames: ['Active Faults (GEM)'],
      highlights: [{ latitude: 6.697, longitude: 125.174, label: 'December mainshock' }],
    },
    {
      id: 'close',
      heading: 'What the record can hold',
      body: [
        'The archive is honest about each of these five events and silent about the thing that '
        + 'connects them. There is no field on an event that says "part of the same sequence as", '
        + 'because deciding that is an interpretation and this platform stores observations.',
        'What it can do is let you see them together: four circles within twenty-nine kilometres, '
        + 'spread across sixty days on the Time Machine. The pattern is visible. The label is not '
        + 'the archive\u2019s to apply.',
      ],
      camera: { latitude: 6.8, longitude: 125.09, zoom: 7.4 },
    },
  ],
  sources: [
    {
      title:
        'Environmental effects following a seismic sequence: the 2019 Cotabato\u2014Davao del Sur '
        + '(Philippines) earthquakes',
      publisher: 'Natural Hazards (Springer)',
      url: 'https://link.springer.com/article/10.1007/s11069-024-06467-7',
      note:
        'Describes the sequence as four earthquakes with MW > 6.0, maximum MW 6.8, and records the '
        + 'triggered landslides and liquefaction this platform holds no data on.',
    },
    {
      title: 'Rupture process of the 2019 Mw 6.8 Davao del Sur earthquake from geodetic and seismic data',
      publisher: 'Geomatics, Natural Hazards and Risk (Taylor & Francis)',
      url: 'https://www.tandfonline.com/doi/full/10.1080/19475705.2024.2416522',
      note:
        'InSAR analysis placing the mainshock on a single NW\u2013SE plane within the Cotabato fault '
        + 'system, with the Makilala\u2013Malungon fault as the causative fault.',
    },
    {
      title: 'The October 2019 Cotabato earthquake sequence: parameters and impacts',
      publisher: 'DOST-PHIVOLCS (via ResearchGate)',
      url: 'https://www.researchgate.net/publication/341368489_The_October_2019_Cotabato_earthquake_sequence_Parameters_and_impacts',
      note:
        'The national network\u2019s own parameters: October events at depths of 7 to 9 km, the first '
        + 'at 7:37 PM PST on 16 October. The source of the depth disagreement described in this story.',
    },
    {
      title: 'Source model of the 2019 Cotabato\u2013Davao del Sur earthquake sequence (EGU24-3482)',
      publisher: 'European Geosciences Union',
      url: 'https://meetingorganizer.copernicus.org/EGU24/EGU24-3482.html?pdf',
      note:
        'Describes the same sequence as five M~6 inland earthquakes of Mw 6.4, 6.6, 5.9, 6.5 and '
        + '6.7 \u2014 the differing count and December magnitude quoted here.',
    },
    {
      title: 'Earthquake catalogue (ComCat)',
      publisher: 'United States Geological Survey',
      url: 'https://earthquake.usgs.gov/earthquakes/search/',
      note:
        'The readings this archive stores, including which depths are agency-assigned rather than '
        + 'measured.',
    },
  ],
};
