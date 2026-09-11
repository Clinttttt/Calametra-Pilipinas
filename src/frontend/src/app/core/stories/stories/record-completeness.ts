import type { Story } from '../story-model';

/**
 * The shape of the record itself, anchored at the largest event in the archive.
 *
 * The only story here that is about the catalogue rather than about a disaster. It exists because
 * this is the archive's largest single trap: the raw event count rises 284-fold across the record,
 * and a reader who takes that at face value concludes that Philippine seismicity is accelerating.
 *
 * Every figure verified against the archive on 2026-09-10, by bucketing `/api/earthquakes/map` by
 * decade:
 *
 *   decade   all events   M6.0+
 *   1900s          21       19      (nine years — the archive begins in 1901)
 *   1910s          56       45
 *   1920s         140       61
 *   1930s         141       50
 *   1940s         109       41
 *   1950s         126       50
 *   1960s         145       27
 *   1970s       1,526       61
 *   1980s       2,526       58
 *   1990s       5,082       64
 *   2000s       5,872       47
 *   2010s       5,523       48
 *   2020s       5,974       60
 *
 * 21 to 5,974 is 284-fold. Over the same span the M6.0+ count per decade sits near 60 from the 1920s
 * onward. Two decades are visibly short even at M6.0+ — the 1900s at 19 and the 1960s at 27 — so the
 * prose says the threshold makes the eras *comparable*, not that it makes them complete.
 *
 * Anchor event, from the same query:
 *
 *   agency id iscgem913230  (ISC-GEM historical catalogue, distributed through USGS ComCat)
 *   date      1918-08-15
 *   magnitude Mw 8.3   — the largest in this archive
 *   epicentre 5.538 N, 123.994 E
 */

/**
 * The ISC-GEM identifier for the 1918 Celebes Sea earthquake.
 *
 * The `iscgem` prefix is itself part of this story: events this old are not USGS-instrumented
 * records but entries in the ISC-GEM historical catalogue, compiled retrospectively from the sparse
 * station coverage of the era. The identifier says so on its face, which the internal id it replaced
 * could not.
 */
const CELEBES_1918_ISC_ID = 'iscgem913230';

export const RECORD_COMPLETENESS: Story = {
  id: 'record-completeness',
  title: 'The archive is not the earth',
  standfirst:
    'This archive holds 21 earthquakes from the 1900s and 5,974 from the 2020s. Seismicity did not '
    + 'increase 284-fold. Instruments did.',
  place: 'The whole archipelago',
  when: '1901 to 2026',
  hazard: 'earthquake',
  shows: 'Why event counts cannot be compared between eras, and which threshold can.',
  beats: [
    {
      id: 'the-largest',
      heading: 'Start with the largest one',
      body: [
        'On 15 August 1918 an earthquake of moment magnitude 8.3 struck near the Moro Gulf coast of '
        + 'Mindanao. It is the largest event in this archive, and it happened in a decade for which '
        + 'the archive holds 56 earthquakes in total.',
        'Fifty-six, for ten years, across an entire archipelago on the Pacific Ring of Fire. The '
        + 'earthquakes were there. The seismometers were not.',
      ],
      camera: { latitude: 5.6, longitude: 124.0, zoom: 6.6 },
      focusEarthquakeAgencyId: CELEBES_1918_ISC_ID,
      highlights: [{ latitude: 5.538, longitude: 123.994, label: '1918 \u00b7 M8.3' }],
    },
    {
      id: 'the-rise',
      heading: 'A 284-fold increase that means nothing',
      body: [
        'Count the events per decade and the archive appears to describe a country shaking harder '
        + 'every year: 21 in the 1900s, 140 in the 1920s, 1,526 in the 1970s, 5,974 in the 2020s.',
        'The 1970s are where the line jumps, and the reason is instrumentation \u2014 the global '
        + 'seismic networks that make a magnitude 4 event in the Sulu Sea detectable at all did not '
        + 'exist in 1930. A rising bar chart of this archive is a chart of monitoring capability.',
        'The Time Machine draws that distribution deliberately, rather than only offering a scrubber. '
        + 'A reader who cannot see the shape of the record cannot allow for it.',
      ],
      camera: { latitude: 12.5, longitude: 122.5, zoom: 4.9 },
    },
    {
      id: 'the-threshold',
      heading: 'What can be compared',
      body: [
        'Restrict the same archive to magnitude 6.0 and above and the picture changes completely. '
        + '61 events in the 1920s. 61 in the 1970s. 64 in the 1990s. 60 in the 2020s.',
        'Large earthquakes were not missed. They were felt across whole regions and recorded '
        + 'worldwide, so the record of them is roughly consistent across a century \u2014 which makes '
        + 'M6.0+ the one subset where an era can honestly be set against another.',
        'That is why the Explore map opens at M6.0+ rather than on the whole catalogue, and why the '
        + 'Time Machine carries the same threshold as a control.',
      ],
      camera: { latitude: 12.5, longitude: 122.5, zoom: 4.9 },
      caveat:
        'Comparable is not complete. Even at M6.0+ two decades are visibly short \u2014 19 events in '
        + 'the 1900s and 27 in the 1960s \u2014 so the threshold makes eras comparable rather than '
        + 'making the early record whole.',
    },
    {
      id: 'the-floor',
      heading: 'And what is missing everywhere',
      body: [
        'There is a second limit, and it applies to the modern record too. Measured across 2015 to '
        + '2026, this archive holds 8,722 events at magnitude 0 and above, 8,722 at magnitude 3.5 and '
        + 'above, and 8,715 at magnitude 4.0 and above \u2014 the same total at every threshold.',
        'The USGS catalogue holds effectively nothing below magnitude 4 in the Philippines. That is a '
        + 'property of a global catalogue, not of the country: the PHIVOLCS national network records '
        + 'magnitude 2 and 3 events routinely, and those records are not in this archive.',
        'So any statement about how many earthquakes happened must name its catalogue. This platform '
        + 'names the source on every reading for exactly this reason.',
      ],
      camera: { latitude: 12.5, longitude: 122.5, zoom: 4.6 },
      caveat:
        'Each data source in Calametra carries a stated minimum reliable magnitude, and the About '
        + 'Data page renders it from the database rather than from documentation.',
    },
    {
      id: 'close',
      heading: 'Reading a hazard archive',
      body: [
        'None of this is a defect in the USGS catalogue, which is an extraordinary scientific asset. '
        + 'It is what a century-long instrumental record inevitably looks like.',
        'The failure would be presenting it as though it were a complete census of Philippine '
        + 'earthquakes. Calametra\u2019s purpose is to make the gaps in the record as visible as the '
        + 'record itself \u2014 because a reader who can see them can reason about them, and a '
        + 'reader who cannot will draw a confident conclusion that is wrong.',
      ],
      camera: { latitude: 12.5, longitude: 122.5, zoom: 5.2 },
    },
  ],
  sources: [
    {
      title: 'Earthquake catalogue (ComCat)',
      publisher: 'United States Geological Survey',
      url: 'https://earthquake.usgs.gov/earthquakes/search/',
      note:
        'The source of every count in this story. Ingested into Calametra for 1901\u20132026 and '
        + 'bucketed by decade from the archive itself.',
    },
    {
      title: 'Philippine earthquake monitoring and the national seismic network',
      publisher: 'DOST-PHIVOLCS',
      url: 'https://www.phivolcs.dost.gov.ph/',
      note:
        'The national network that records the magnitude 2\u20133 events absent from the global '
        + 'catalogue. Cited because their absence here is a property of the source, not of the '
        + 'seismicity.',
    },
    {
      title: '1918 Celebes Sea earthquake',
      publisher: 'United States Geological Survey (ComCat event record)',
      url: 'https://earthquake.usgs.gov/earthquakes/search/',
      note:
        'The anchor event: moment magnitude 8.3 on 15 August 1918, the largest in this archive.',
    },
  ],
};
