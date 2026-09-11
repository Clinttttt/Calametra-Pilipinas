import type { Story } from '../story-model';

/**
 * Deep-focus seismicity beneath the southern Philippines.
 *
 * Chosen because the Explore map is a plan projection, and this is the one property of the archive a
 * plan projection cannot express at all. Two circles drawn side by side can be ten kilometres and six
 * hundred and forty kilometres below the surface. Nothing about their placement says so.
 *
 * It is also the story that justifies the cross-section, which is otherwise the platform's least
 * self-evident feature: a depth-versus-distance plot looks like an odd choice until you know that
 * 591 events in this archive are deeper than 500 km.
 *
 * ── Verified against the archive on 2026-09-10 ──────────────────────────────
 *
 *   SELECT count(*), count(*) FILTER (WHERE depth_kilometres > 70), ... 
 *   FROM earthquake_observations WHERE depth_kilometres IS NOT NULL;
 *
 *   27,240 observations carry a depth
 *    7,051 deeper than  70 km
 *      774 deeper than 300 km
 *      591 deeper than 500 km
 *      157 deeper than 600 km
 *      678.0 km  the deepest in the archive
 *
 * Anchor event, the largest deep earthquake in the record:
 *
 *   agency id us7000myfa  (USGS ComCat)
 *   origin    2024-07-11 02:13 UTC  =  10:13 Philippine time
 *   magnitude Mww 7.1
 *   depth     639.5 km, quality 1 \u2014 measured, not assigned
 *   epicentre 6.084 N, 123.150 E   (Moro Gulf, west of Mindanao)
 *
 * The deepest single reading, for contrast:
 *
 *   usp0009qj4  2000-03-31 07:29 UTC  Mb 5.1  678.0 km  Constrained  7.561 N 123.642 E
 *
 * The Cotabato Trench cross-section preset, queried through the platform's own endpoint at 7.0°N
 * from 121.5°E to 124.5°E with a 60 km corridor:
 *
 *   402 points with a measured depth   (172 assigned depths excluded)
 *   663.2 km  deepest measured point on the section
 *   190 points shallower than  70 km
 *   196 points deeper    than 300 km
 *   180 points deeper    than 500 km
 *
 * That distribution is the finding: the section is bimodal. A shallow band and a deep cluster, with
 * roughly thirty points spread through the four hundred kilometres between them. The prose says so
 * and the reader can open the same preset and look.
 *
 * ── External figures, each cited in `sources` ───────────────────────────────
 *
 *   GJI (1980)       "In this area most of the seismicity is at depths greater than 500 km,
 *                    associated with the westward dipping Benioff zones of the Sangihe and
 *                    Mindanao arc systems" \u2014 written of the same region as the anchor event
 *   G-cubed (2018)   Philippine Sea slab subducted to 450\u2013600 km with an overturned dip angle
 *                    along the southern segment of the Philippine Trench
 *
 * The 1980 paper is the same one the Moro Gulf story cites for its magnitude spread. It is used here
 * for a different sentence, which is worth noting: that paper's subject was a *shallow* event, and it
 * remarks on the shallowness precisely because the surrounding seismicity is so deep.
 *
 * ⚠ Deliberately not claimed: anything about how strongly these earthquakes were felt. Depth governs
 * that, and it is the obvious thing to say \u2014 but this platform holds no intensity, ShakeMap or
 * felt-report data, so saying it would mean asserting a figure from outside the record. The story
 * makes the geometric point and stops.
 */

/** The largest deep earthquake in the archive: Mww 7.1 at a measured 639.5 km. */
const DEEP_2024_USGS_ID = 'us7000myfa';

/** The deepest reading in the archive, at 678 km. */
const DEEPEST_USGS_ID = 'usp0009qj4';

export const DEEP_FOCUS: Story = {
  id: 'deep-focus',
  title: 'Six hundred kilometres down',
  standfirst:
    'In July 2024 a magnitude 7.1 earthquake occurred beneath the Moro Gulf at a measured depth of '
    + '639 kilometres. On a map it is a circle like any other. Five hundred and ninety others in '
    + 'this archive are deeper than five hundred kilometres.',
  place: 'Moro Gulf and western Mindanao',
  when: '1901 to 2026',
  hazard: 'earthquake',
  shows: 'That a map is a plan projection, and depth is the dimension it discards.',
  beats: [
    {
      id: 'the-circle',
      heading: 'An ordinary-looking circle',
      body: [
        'On 11 July 2024, at 10:13 in the morning Philippine time, a magnitude 7.1 earthquake '
        + 'occurred beneath the Moro Gulf. By magnitude it is among the largest events in the modern '
        + 'record here.',
        'Its depth was 639.5 kilometres, and that figure was resolved rather than assigned \u2014 '
        + 'which matters, because at this depth an assigned value would be worthless.',
      ],
      camera: { latitude: 6.08, longitude: 123.15, zoom: 7.6 },
      focusEarthquakeAgencyId: DEEP_2024_USGS_ID,
      highlights: [{ latitude: 6.084, longitude: 123.15, label: 'M7.1 at 639 km' }],
    },
    {
      id: 'what-depth-means',
      heading: 'What that number is doing on a flat map',
      body: [
        'Six hundred and thirty-nine kilometres is farther than Manila to Tacloban. It is roughly a '
        + 'hundred times the depth of the deepest mine ever dug, and well below the base of the '
        + 'crust, which under the Philippines is a few tens of kilometres thick.',
        'The Explore map places this event by longitude and latitude, and by nothing else. A '
        + 'magnitude 6 at 10 kilometres and this magnitude 7.1 at 639 kilometres are drawn on the '
        + 'same plane. Colour carries depth on that map for exactly this reason \u2014 but colour is a '
        + 'label, not a position.',
      ],
      camera: { latitude: 6.5, longitude: 123.4, zoom: 6.4 },
      focusEarthquakeAgencyId: DEEP_2024_USGS_ID,
    },
    {
      id: 'how-many',
      heading: 'How much of the archive is down there',
      body: [
        'Of the 27,240 readings in this archive that carry a depth, 7,051 are deeper than 70 '
        + 'kilometres \u2014 below the crust, in the descending slab. 774 are deeper than 300 '
        + 'kilometres. 591 are deeper than 500. And 157 are deeper than 600.',
        'The deepest is 678 kilometres: a magnitude 5.1 recorded in March 2000, west of Mindanao. '
        + 'That is close to the maximum depth at which earthquakes occur anywhere on Earth.',
        'This is not a handful of curiosities. It is a fifth of the record sitting below the crust, '
        + 'and it is invisible in plan view.',
      ],
      camera: { latitude: 7.0, longitude: 123.5, zoom: 6.8 },
      focusEarthquakeAgencyId: DEEPEST_USGS_ID,
      highlights: [{ latitude: 7.561, longitude: 123.642, label: 'Deepest: 678 km' }],
    },
    {
      id: 'the-section',
      heading: 'So look at it side-on',
      body: [
        'This is what the cross-section is for. The Cotabato Trench profile runs west to east along '
        + '7 degrees north, and plots every hypocentre within sixty kilometres of that line against '
        + 'distance along it and depth below it.',
        'Four hundred and two events on that section have a measured depth. A hundred and ninety are '
        + 'shallower than seventy kilometres. A hundred and eighty are deeper than five hundred. '
        + 'Barely thirty occupy the four hundred kilometres in between.',
        'That gap is not a sampling artefact of this platform. It is the shape of a slab: a shallow '
        + 'seismogenic band near the surface, and a deep cluster where the descending plate is being '
        + 'deformed hundreds of kilometres down.',
      ],
      camera: { latitude: 7.0, longitude: 123.0, zoom: 6.6 },
      crossSectionPresetId: 'cotabato-trench',
      caveat:
        'The section excludes agency-assigned depths by default \u2014 172 of them here. On a depth '
        + 'plot an assigned depth is a fabricated vertical position, and four of them would draw flat '
        + 'lines that look like structure.',
    },
    {
      id: 'why-deep',
      heading: 'Why the south is so deep',
      body: [
        'Western Mindanao and the Moro Gulf sit above more than one descending slab. A 1980 study of '
        + 'the region observed that most of its seismicity lies deeper than five hundred kilometres, '
        + 'associated with the westward-dipping zones of the Sangihe and Mindanao arc systems. '
        + 'Seismic tomography has since traced the Philippine Sea slab to depths of 450 to 600 '
        + 'kilometres beneath the southern Philippine Trench, with an overturned dip.',
        'The archive reflects that geometry without being told it. Nobody encoded a slab into this '
        + 'database; it emerges from plotting where earthquakes actually are.',
      ],
      camera: { latitude: 6.4, longitude: 123.6, zoom: 6.2 },
      focusEarthquakeAgencyId: DEEP_2024_USGS_ID,
    },
    {
      id: 'close',
      heading: 'What the platform will not tell you',
      body: [
        'The obvious next question is how strongly a magnitude 7.1 at 639 kilometres was felt at the '
        + 'surface, and the honest answer is that Calametra does not know. It holds origins, '
        + 'magnitudes, depths and provenance. It holds no intensity reports, no ShakeMap, no felt '
        + 'data.',
        'Depth is one of the strongest controls on shaking, so the temptation to add a sentence about '
        + 'it is real. That sentence would be sourced from general knowledge rather than from this '
        + 'archive, and the platform\u2019s rule is that prose supplies context while the archive '
        + 'supplies numbers.',
      ],
      camera: { latitude: 6.8, longitude: 123.4, zoom: 5.8 },
    },
  ],
  sources: [
    {
      title:
        '1976 August 16, Mindanao, Philippine earthquake (Ms = 7.8) \u2014 evidence for a subduction '
        + 'zone south of Mindanao',
      publisher: 'Geophysical Journal International (Oxford University Press)',
      url: 'https://academic.oup.com/gji/article/57/1/51/716416',
      note:
        'States that most seismicity in this region lies deeper than 500 km, associated with the '
        + 'westward-dipping Benioff zones of the Sangihe and Mindanao arc systems.',
    },
    {
      title:
        'Evolution of the southern segment of the Philippine Trench: constraints from seismic '
        + 'tomography',
      publisher: 'Geochemistry, Geophysics, Geosystems (AGU)',
      url: 'https://agupubs.onlinelibrary.wiley.com/doi/full/10.1029/2018gc007685',
      note:
        'Traces the Philippine Sea slab to depths of 450\u2013600 km with an overturned dip angle along '
        + 'the southern segment of the Philippine Trench.',
    },
    {
      title: 'Seismotectonics of the Philippine and Taiwan subduction systems',
      publisher: 'Geochemistry, Geophysics, Geosystems (AGU)',
      url: 'https://agupubs.onlinelibrary.wiley.com/doi/10.1029/2023GC010990',
      note:
        'Regional context for the subduction and collision systems that produce the depth '
        + 'distribution described here.',
    },
    {
      title: 'Earthquake catalogue (ComCat)',
      publisher: 'United States Geological Survey',
      url: 'https://earthquake.usgs.gov/earthquakes/search/',
      note:
        'The depth readings counted in this story, including which of them were measured rather '
        + 'than assigned.',
    },
  ],
};
