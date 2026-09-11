import type { Story } from '../story-model';

/**
 * The 1990 Luzon earthquake.
 *
 * Chosen because it is the clearest demonstration that a catalogue entry is a point and an
 * earthquake is not. The rupture reached the surface and was walked and measured on the ground, so
 * the gap between "one dot" and "what happened" is documented rather than inferred.
 *
 * Verified against the archive on 2026-09-10 via `/api/earthquakes/map`:
 *
 *   agency id usp0004bxs  (USGS ComCat)
 *   date      1990-07-16
 *   magnitude Mw 7.7
 *   depth     25.1 km, quality 1 — measured
 *   epicentre 15.679 N, 121.172 E
 *
 * Note the depth here *is* measured, which is why this story is paired with Moro Gulf rather than
 * repeating it: the limitation on show is different.
 *
 * External figures, each cited in `sources`:
 *
 *   USGS OFR 92-367   Digdig faults ruptured at least 110 km; surface displacement up to 6.2 m
 *                     left-lateral horizontal, under 1 m vertical
 *   Published field    a 125 km ground rupture from Dingalan, Aurora to Kayapa, Nueva Vizcaya
 *   mapping            (surface-wave magnitude 7.8 in the same accounts)
 *
 * The 110 km and 125 km figures are both cited. They are not in conflict: one is the minimum length
 * traced along the Digdig fault, the other the full extent across the fault system including its
 * splays. The prose gives both rather than averaging them.
 */
const LUZON_USGS_ID = 'usp0004bxs';

export const LUZON_1990: Story = {
  id: 'luzon-1990',
  title: 'The earthquake that was not a point',
  standfirst:
    'The archive records this earthquake as a single dot in central Luzon. The ground broke for at '
    + 'least 110 kilometres, and moved sideways by up to six metres. Both statements are accurate.',
  place: 'Central Luzon',
  when: '16 July 1990',
  hazard: 'earthquake',
  shows: 'Why an epicentre is a computed point, not the location of an earthquake.',
  beats: [
    {
      id: 'the-dot',
      heading: 'One dot on a map',
      body: [
        'On 16 July 1990 a magnitude 7.7 earthquake struck central Luzon. This archive places it at '
        + '15.679 north, 121.172 east, at a depth of 25.1 kilometres \u2014 and unlike most events '
        + 'in the record, that depth was genuinely resolved rather than assigned.',
        'One point, three numbers. That is what a catalogue entry is.',
      ],
      camera: { latitude: 15.68, longitude: 121.17, zoom: 8.2 },
      focusEarthquakeAgencyId: LUZON_USGS_ID,
      highlights: [{ latitude: 15.679, longitude: 121.172, label: 'Catalogued epicentre' }],
    },
    {
      id: 'the-hypocentre',
      heading: 'What the point actually means',
      body: [
        'An epicentre is not where the earthquake was. It is the point on the surface above where '
        + 'the rupture is calculated to have started, derived by solving for a position from arrival '
        + 'times at recording stations.',
        'Rupture begins at that point and then travels. For an event of this size it travels for '
        + 'tens of seconds and tens or hundreds of kilometres, releasing energy the whole way. The '
        + 'catalogue records the beginning and says nothing about the rest.',
      ],
      camera: { latitude: 15.679, longitude: 121.172, zoom: 9.8 },
      focusEarthquakeAgencyId: LUZON_USGS_ID,
      highlights: [{ latitude: 15.679, longitude: 121.172, label: 'Rupture started here' }],
    },
    {
      id: 'the-rupture',
      heading: 'A hundred and ten kilometres of it',
      body: [
        'This rupture reached the surface, which means it could be walked. A United States '
        + 'Geological Survey field report records that the Digdig fault ruptured over a distance of '
        + 'at least 110 kilometres, with surface displacements as great as 6.2 metres of '
        + 'left-lateral horizontal movement and under a metre of vertical movement. Other published '
        + 'mapping traces a 125-kilometre ground rupture running from Dingalan in Aurora to Kayapa '
        + 'in Nueva Vizcaya, across the Philippine Fault system and its splays.',
        'The fault traces are now drawn on the map. The Digdig fault is among them, from the GEM '
        + 'global compilation.',
        'Set the length of that line against the size of the dot. Nothing in the catalogue records '
        + 'the difference, and no plotting of catalogue points can recover it.',
      ],
      camera: { latitude: 16.0, longitude: 120.9, zoom: 7.4 },
      focusEarthquakeAgencyId: LUZON_USGS_ID,
      layerNames: ['Active Faults (GEM)'],
      highlights: [{ latitude: 15.679, longitude: 121.172, label: 'Epicentre' }],
      caveat:
        'The 110 km and 125 km figures are both cited and are not in conflict: one is the minimum '
        + 'length traced along the Digdig fault, the other the full extent across the fault system.',
    },
    {
      id: 'named-fault',
      heading: 'Naming the fault, carefully',
      body: [
        'This is one of the few Philippine earthquakes where the structure that moved can be named '
        + 'with confidence, and the reason is field evidence rather than catalogue proximity: the '
        + 'rupture was visible at the surface and was mapped along the Digdig fault.',
        'That is worth stating because the platform refuses to make the same claim elsewhere. '
        + 'Elsewhere in Calametra, a fault trace near an epicentre is shown as exactly that \u2014 '
        + 'proximity \u2014 and never as attribution. Here, the attribution comes from someone who '
        + 'went and looked.',
      ],
      camera: { latitude: 15.9, longitude: 121.0, zoom: 8.4 },
      layerNames: ['Active Faults (GEM)'],
      highlights: [{ latitude: 15.679, longitude: 121.172, label: 'Epicentre' }],
      caveat:
        'GEM traces are a global compilation and are coarser than the national mapping. PHIVOLCS '
        + 'publishes more detailed traces, which this platform displays as imagery only pending a '
        + 'signed data agreement.',
    },
    {
      id: 'close',
      heading: 'What to take from the dot',
      body: [
        'Every circle on the Explore map is a point of this kind. For a magnitude 4 event the '
        + 'difference between the point and the rupture is negligible. For a magnitude 7.7 it is the '
        + 'difference between one place and two provinces.',
        'The map sizes its circles by magnitude, which is the honest way to hint at that without '
        + 'claiming an extent the data does not contain.',
      ],
      camera: { latitude: 15.6, longitude: 121.2, zoom: 6.4 },
    },
  ],
  sources: [
    {
      title: 'The 16 July 1990 Luzon earthquake, Philippines (Open-File Report 92-367)',
      publisher: 'United States Geological Survey',
      url: 'https://pubs.usgs.gov/of/1992/0367a/report.pdf',
      note:
        'Digdig faults ruptured at least 110 km; surface displacement up to 6.2 m left-lateral '
        + 'horizontal and under 1 m vertical.',
    },
    {
      title: 'The 16 July 1990 Luzon earthquake ground rupture',
      publisher: 'Published field mapping (via ResearchGate)',
      url: 'https://www.researchgate.net/publication/333679705_The_16_July_1990_Luzon_Earthquake_Ground_Rupture',
      note:
        '125 km ground rupture from Dingalan, Aurora to Kayapa, Nueva Vizcaya along the Philippine '
        + 'Fault Zone and the Digdig fault; surface-wave magnitude 7.8.',
    },
    {
      title: 'Global Active Faults database',
      publisher: 'GEM Foundation',
      url: 'https://github.com/GEMScienceTools/gem-global-active-faults',
      note:
        'The fault traces drawn in this story, including the Digdig fault. CC BY-SA 4.0, which is '
        + 'why Calametra can store and query them.',
    },
    {
      title: 'Earthquake catalogue (ComCat)',
      publisher: 'United States Geological Survey',
      url: 'https://earthquake.usgs.gov/earthquakes/search/',
      note: 'The reading this archive stores: Mw 7.7 at a measured depth of 25.1 km.',
    },
  ],
};
