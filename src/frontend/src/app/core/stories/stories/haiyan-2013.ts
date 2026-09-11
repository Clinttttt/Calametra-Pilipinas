import type { Story } from '../story-model';

/**
 * Typhoon Haiyan, known in the Philippines as Yolanda.
 *
 * Chosen because it is the cyclone equivalent of the Surigao story: four agencies analysed the same
 * storm and reached four different peak intensities, and the reason is method rather than
 * disagreement about the weather. It is also the case where the platform's wind-versus-pressure
 * asymmetry is starkest.
 *
 * Every figure below was read from this archive on 2026-09-10 via
 * `/api/cyclones/01a07f74-a9a3-7730-bcfc-8a0214a36dc0`, and re-verified on 2026-09-10 through
 * `/api/cyclones/external/2013306N07162` after this story was re-keyed onto the IBTrACS SID — the
 * two routes return byte-identical payloads. Nothing is from memory.
 *
 *   agency                                   period   peak wind   min pressure   fixes
 *   Joint Typhoon Warning Center             1-min     170 kt      895 mb          73
 *   Hong Kong Observatory                    10-min    155 kt      890 mb          67
 *   China Meteorological Administration      2-min     151 kt      890 mb          67
 *   Japan Meteorological Agency              10-min    125 kt      895 mb          65
 *
 *   wind spread      45 kt   (125 to 170)
 *   pressure spread   5 mb   (890 to 895)
 *
 * ⚠ An earlier note in this project claimed the four agencies reported an *identical* 895 mb. That
 * was wrong, and checking the endpoint is what caught it. Two report 890 and two report 895. The
 * asymmetry is still the point — 45 knots against 5 millibars — but it is a narrow spread, not
 * unanimity, and the prose says so.
 *
 * Peak fix, and the four Philippine coast crossings, all from the JTWC track in this archive:
 *
 *   2013-11-07 18:00 UTC   10.6 N 126.9 E   170 kt   895 mb   eyewall radius 17 nmi
 *   2013-11-07 21:00 UTC   10.8 N 125.8 E   168 kt   897 mb   crosses coast
 *   2013-11-08 00:00 UTC   11.0 N 124.7 E   165 kt   899 mb   crosses coast
 *   2013-11-08 03:00 UTC   11.2 N 123.6 E   155 kt   907 mb   crosses coast
 *   2013-11-08 06:00 UTC   11.4 N 122.5 E   145 kt   914 mb   crosses coast
 */
/**
 * Haiyan's IBTrACS storm identifier.
 *
 * Reads as season, day of year and the position of first detection: 2013, day 306, first seen near
 * 7°N 162°E. Assigned by the compiler of the archive rather than by any one agency, which is why all
 * four agencies' fixes for this storm carry it — and why it, rather than the name, is what durable
 * content can safely reference. "HAIYAN" is a warning-centre label; "Yolanda" is PAGASA's. The SID
 * is neither, and outlives both.
 */
const HAIYAN_SID = '2013306N07162';

export const HAIYAN_2013: Story = {
  id: 'haiyan-2013',
  title: 'Four agencies, four intensities',
  standfirst:
    'Four meteorological agencies analysed Typhoon Haiyan. Their peak wind speeds differ by 45 '
    + 'knots. Their central pressures differ by 5 millibars. The gap between those two numbers is '
    + 'the whole problem with comparing storms.',
  place: 'Central Philippines',
  when: '7\u20138 November 2013',
  hazard: 'cyclone',
  shows: 'Why peak wind is not comparable across agencies, and why pressure very nearly is.',
  beats: [
    {
      id: 'approach',
      heading: 'A storm crossing an archipelago',
      body: [
        'Haiyan \u2014 Yolanda to PAGASA, which names every storm entering Philippine waters \u2014 '
        + 'approached from the east and crossed the central Philippines on 7 and 8 November 2013.',
        'Each agency\u2019s track is drawn separately on the map. There is no averaged path here, '
        + 'because a mean of four analyses is a line none of them published.',
      ],
      camera: { latitude: 11.2, longitude: 125.0, zoom: 5.9 },
      focusCycloneStormId: HAIYAN_SID,
    },
    {
      id: 'peak',
      heading: 'One hundred and seventy knots, or one hundred and twenty-five',
      body: [
        'At 18:00 UTC on 7 November, just east of Mindanao, the Joint Typhoon Warning Center '
        + 'recorded a sustained wind of 170 knots and a central pressure of 895 millibars. The eye '
        + 'was tight: a radius of maximum wind of 17 nautical miles.',
        'For the same storm, the Japan Meteorological Agency\u2019s peak is 125 knots. Hong Kong '
        + 'reports 155. China reports 151.',
        'Nobody is wrong. JTWC averages wind over one minute; JMA and Hong Kong over ten; China '
        + 'over two. A shorter averaging window preserves brief gusts that a longer one smooths '
        + 'away, so the four numbers are measurements of four different quantities.',
      ],
      camera: { latitude: 10.6, longitude: 126.9, zoom: 7.4 },
      focusCycloneStormId: HAIYAN_SID,
      highlights: [{ latitude: 10.6, longitude: 126.9, label: 'Peak \u00b7 170 kt, 895 mb' }],
      caveat:
        'No conversion factor reconciles these. Elsewhere in this archive the one-minute reading is '
        + 'the *lower* of two, which is impossible if a fixed ratio applied.',
    },
    {
      id: 'pressure',
      heading: 'And yet they nearly agree on pressure',
      body: [
        'The same four agencies report a minimum central pressure of 895, 890, 890 and 895 '
        + 'millibars. A spread of five millibars, against forty-five knots of wind.',
        'The reason is that pressure is the same quantity to everyone. There is no averaging '
        + 'interval to choose, so there is no method left in the number \u2014 only the storm and '
        + 'the measurement error.',
        'This is why Calametra orders its storm list by lowest central pressure rather than by '
        + 'strongest wind. Ordering by wind would rank whichever agency uses the shortest averaging '
        + 'interval, and the list would be sorted by method as much as by intensity.',
      ],
      camera: { latitude: 10.7, longitude: 126.0, zoom: 6.8 },
      focusCycloneStormId: HAIYAN_SID,
      highlights: [{ latitude: 10.6, longitude: 126.9, label: '895 mb \u00b7 JTWC' }],
    },
    {
      id: 'landfalls',
      heading: 'Four coastlines in nine hours',
      body: [
        'The JTWC track in this archive crosses the coast four times between 21:00 UTC on the 7th '
        + 'and 06:00 UTC on the 8th: Eastern Samar, then Leyte, then the islands south of Cebu, then '
        + 'Panay.',
        'The wind reading falls at each crossing \u2014 168, 165, 155, 145 knots \u2014 and the '
        + 'central pressure rises from 897 to 914 millibars. Land does that to a storm: it cuts off '
        + 'the warm water the circulation is drawing on.',
        'Each crossing is marked. They are the agency\u2019s own fixes, six and three hours apart, '
        + 'not an interpolation.',
      ],
      camera: { latitude: 11.1, longitude: 124.2, zoom: 7.0 },
      focusCycloneStormId: HAIYAN_SID,
      highlights: [
        { latitude: 10.8, longitude: 125.8, label: '168 kt' },
        { latitude: 11.0, longitude: 124.7, label: '165 kt' },
        { latitude: 11.2, longitude: 123.6, label: '155 kt' },
        { latitude: 11.4, longitude: 122.5, label: '145 kt' },
      ],
    },
    {
      id: 'close',
      heading: 'What this archive holds about Haiyan',
      body: [
        'Four tracks, 272 fixes between them, wind qualified by its averaging period, pressure, and '
        + 'the measured extent of the wind field where an agency published it.',
        'It holds nothing about what happened to Tacloban. No storm surge heights, no casualty '
        + 'figures, no damage assessments \u2014 those are held by PAGASA, NDRRMC and the agencies '
        + 'listed in the sources, and this platform does not reproduce them.',
        'That boundary is deliberate. A tool that mixed a wind reading it can attribute with a '
        + 'casualty figure it cannot would leave a reader unable to tell which was which.',
      ],
      camera: { latitude: 11.5, longitude: 123.0, zoom: 6.2 },
      focusCycloneStormId: HAIYAN_SID,
      caveat:
        'PAGASA names every storm entering the Philippine Area of Responsibility. Calametra\u2019s '
        + 'international-to-local crosswalk is hand-transcribed and covers significant storms, so a '
        + 'missing local name is a gap in this platform rather than a storm without one.',
    },
  ],
  sources: [
    {
      title: 'International Best Track Archive for Climate Stewardship (IBTrACS), v04r01',
      publisher: 'NOAA National Centers for Environmental Information',
      url: 'https://www.ncei.noaa.gov/products/international-best-track-archive',
      note:
        'Every wind, pressure, position and wind-field radius in this story. IBTrACS collects each '
        + 'agency\u2019s own best track rather than reconciling them, which is what makes the '
        + 'disagreement visible.',
    },
    {
      title: 'Tropical cyclone information and PAGASA naming',
      publisher: 'DOST-PAGASA',
      url: 'https://www.pagasa.dost.gov.ph/climate/tropical-cyclone-information',
      note:
        'The naming authority for storms in the Philippine Area of Responsibility, and the source of '
        + 'the local name Yolanda. Impact and warning records are held here, not in Calametra.',
    },
    {
      title: 'Joint Typhoon Warning Center best track archive',
      publisher: 'US Naval Oceanography Portal',
      url: 'https://www.metoc.navy.mil/jtwc/jtwc.html',
      note:
        'The one-minute sustained wind analysis, including the 170 kt peak and the 17 nmi radius of '
        + 'maximum wind.',
    },
    {
      title: 'Disaster response and impact reporting',
      publisher: 'NDRRMC',
      url: 'https://ndrrmc.gov.ph/',
      note:
        'Casualty, displacement and damage records. Cited because this story deliberately does not '
        + 'narrate them.',
    },
  ],
};
