import type { Story } from '../story-model';

/**
 * The 2013 Bohol earthquake.
 *
 * The most important story in this catalogue for a platform that draws fault traces, because the
 * fault that produced this earthquake was not on any map before it happened. Every other story here
 * argues that proximity is not attribution; this one shows that the candidate list itself can be
 * incomplete.
 *
 * Verified against the archive on 2026-09-10 via `/api/earthquakes/map`, and re-verified on
 * 2026-09-10 through `/api/earthquakes/external/usb000kdb4` after this story was re-keyed onto the
 * agency identifier:
 *
 *   agency id usb000kdb4  (USGS ComCat)
 *   origin    2013-10-15 00:12 UTC  =  08:12 Philippine time
 *   magnitude Mww 7.1
 *   depth     19 km, quality 1 — measured
 *   epicentre 9.8796 N, 124.1167 E
 *
 * Published accounts give Mw 7.2 where this archive holds Mww 7.1. Both are moment magnitudes; the
 * difference is revision and analysis, and the story states the archive's own figure alongside the
 * published one rather than silently adopting either.
 *
 * External figures, each cited in `sources`:
 *
 *   NHESS            originated at 12 km depth on an unmapped reverse fault; surface expression over
 *                    several kilometres; maximum vertical displacement 3 m
 *   Tectonics (2019) ground rupture associated with pre-existing scarps of the previously unmapped,
 *                    Quaternary-active North Bohol Fault
 *   Geosci. Letters  the fault was named the North Bohol Fault after the event
 *   LiDAR mapping    fault scarp 3 m high at Barangay Anonang, Inabanga, mean orientation N51°E
 *
 * Note the depth disagreement is itself instructive: this archive holds 19 km, the NHESS analysis
 * gives 12 km. Both are measured values from different inversions.
 */
const BOHOL_USGS_ID = 'usb000kdb4';

export const BOHOL_2013: Story = {
  id: 'bohol-2013',
  title: 'The fault that was not on the map',
  standfirst:
    'Calametra can show you every fault trace it holds near an epicentre. In October 2013 the fault '
    + 'that moved was not in any of them, because nobody had mapped it yet.',
  place: 'Bohol, Central Visayas',
  when: '15 October 2013',
  hazard: 'earthquake',
  shows: 'That the list of known faults is itself incomplete.',
  beats: [
    {
      id: 'morning',
      heading: 'Twelve minutes past eight',
      body: [
        'At 00:12 UTC on 15 October 2013 \u2014 twelve minutes past eight in the morning locally \u2014 '
        + 'an earthquake struck Bohol. This archive records a moment magnitude of 7.1 at a measured '
        + 'depth of 19 kilometres. Published analyses give 7.2, and one inversion places the origin '
        + 'at 12 kilometres.',
        'It was an inland earthquake, not a subduction event offshore. That distinction is the whole '
        + 'story: an inland rupture means a crustal fault, and a crustal fault is the kind of thing '
        + 'that appears on a hazard map.',
      ],
      camera: { latitude: 9.88, longitude: 124.12, zoom: 8.6 },
      focusEarthquakeAgencyId: BOHOL_USGS_ID,
      highlights: [{ latitude: 9.8796, longitude: 124.1167, label: 'Epicentre' }],
    },
    {
      id: 'known-faults',
      heading: 'What the maps held',
      body: [
        'The fault traces Calametra can draw are now switched on. They come from the GEM global '
        + 'compilation, which is openly licensed and therefore storable and measurable.',
        'Ask the platform which mapped fault is nearest this epicentre and it will answer. The answer '
        + 'would have been wrong \u2014 not because the measurement is wrong, but because the '
        + 'structure that ruptured was not among the candidates.',
      ],
      camera: { latitude: 9.9, longitude: 124.15, zoom: 8.9 },
      focusEarthquakeAgencyId: BOHOL_USGS_ID,
      layerNames: ['Active Faults (GEM)'],
      highlights: [{ latitude: 9.8796, longitude: 124.1167, label: 'Epicentre' }],
    },
    {
      id: 'unmapped',
      heading: 'A reverse fault nobody had recorded',
      body: [
        'The rupture reached the surface. Field teams traced a scarp up to three metres high near '
        + 'Barangay Anonang in the municipality of Inabanga, and mapped its continuation along '
        + 'pre-existing scarps that had not been catalogued as an active fault.',
        'The structure was subsequently named the North Bohol Fault. Before 15 October 2013 it did '
        + 'not exist in any hazard map, in any fault database, or in this archive.',
        'Deformation associated with the event extended along roughly the sixty-kilometre length of '
        + 'the island \u2014 a structure of that scale, invisible to the record until it moved.',
      ],
      camera: { latitude: 9.98, longitude: 124.06, zoom: 9.6 },
      focusEarthquakeAgencyId: BOHOL_USGS_ID,
      layerNames: ['Active Faults (GEM)'],
      highlights: [{ latitude: 10.0, longitude: 124.06, label: 'Inabanga \u00b7 surface rupture' }],
      caveat:
        'The Inabanga highlight marks the municipality named in the field reports, not a surveyed '
        + 'scarp position. Calametra does not hold the rupture trace \u2014 it is not in the GEM '
        + 'compilation this platform can store.',
    },
    {
      id: 'what-it-means',
      heading: 'What an absence on a map means',
      body: [
        'This is why Calametra never states that a fault caused an earthquake. Drawing the nearest '
        + 'trace and letting proximity imply attribution would have been wrong here in the strongest '
        + 'possible way: the correct answer was not on the list at all.',
        'It is also why the platform distinguishes the GEM compilation from the national mapping, and '
        + 'says which it is showing. GEM is coarser. PHIVOLCS holds more detail. Neither held this '
        + 'fault in 2013, and neither can be assumed complete now.',
        'An empty area on a fault map means nobody has mapped a fault there. It does not mean there '
        + 'is none.',
      ],
      camera: { latitude: 9.92, longitude: 124.1, zoom: 8.2 },
      layerNames: ['Active Faults (GEM)'],
      highlights: [{ latitude: 9.8796, longitude: 124.1167, label: 'Epicentre' }],
    },
    {
      id: 'close',
      heading: 'The limit of a catalogue',
      body: [
        'Every other story here is about values that disagree \u2014 two magnitudes, two depths, four '
        + 'peak winds. This one is about something harder: a value that was absent, and whose absence '
        + 'looked exactly like the absence of a hazard.',
        'A tool that presented its fault layer as a complete inventory of Philippine faults would have '
        + 'been confidently, invisibly wrong about Bohol until the morning of 15 October 2013.',
      ],
      camera: { latitude: 10.0, longitude: 124.2, zoom: 7.2 },
    },
  ],
  sources: [
    {
      title:
        'The 15 October 2013 Bohol earthquake: rupture on an unmapped reverse fault (NHESS '
        + 'discussion paper)',
      publisher: 'Natural Hazards and Earth System Sciences / Copernicus',
      url: 'https://nhess.copernicus.org/preprints/2/2103/2014/nhessd-2-2103-2014-print.pdf',
      note:
        'Origin at 12 km depth on an unmapped reverse fault, surface expression over several '
        + 'kilometres, maximum vertical displacement 3 m.',
    },
    {
      title:
        'Coseismic Ground Rupture of the 15 October 2013 Mw 7.2 Bohol Earthquake, Bohol Island, '
        + 'Central Philippines',
      publisher: 'Tectonics (via NASA ADS)',
      url: 'https://ui.adsabs.harvard.edu/abs/2019Tecto..38.2558R',
      note:
        'The ground rupture is associated with pre-existing scarps of the previously unmapped, '
        + 'Quaternary-active North Bohol Fault.',
    },
    {
      title:
        'Shallow seismic reflection imaging of the Inabanga\u2013Clarin portion of the North Bohol '
        + 'Fault, Central Visayas, Philippines',
      publisher: 'Geoscience Letters (Springer)',
      url: 'https://link.springer.com/article/10.1186/s40562-019-0139-x',
      note:
        'Confirms the fault was previously unidentified and was named the North Bohol Fault by '
        + 'authorities after the earthquake.',
    },
    {
      title:
        'Mapping of the Inabanga Fault in Bohol using high-resolution LiDAR imagery and field '
        + 'mapping verification',
      publisher: 'Published field and LiDAR mapping (via ResearchGate)',
      url: 'https://www.researchgate.net/publication/370903979_Mapping_of_the_Inabanga_Fault_in_Bohol_Philippines_using_High_Resolution_LIDAR_Imagery_and_Field_Mapping_Verifica',
      note:
        'The 3 m fault scarp at Barangay Anonang, Inabanga, with a mean principal orientation of '
        + 'N51\u00b0E.',
    },
    {
      title: 'Earthquake catalogue (ComCat)',
      publisher: 'United States Geological Survey',
      url: 'https://earthquake.usgs.gov/earthquakes/search/',
      note: 'The reading this archive stores: Mww 7.1 at a measured depth of 19 km.',
    },
  ],
};
