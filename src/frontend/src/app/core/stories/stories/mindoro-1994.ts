import type { Story } from '../story-model';

/**
 * The 1994 Mindoro earthquake and tsunami.
 *
 * Chosen because it is the case where a well-recorded earthquake does not account for what followed.
 * The rupture is mapped, the magnitude is agreed, the depth is measured — and the tsunami runup is
 * still larger than that rupture alone explains, which is an open question in the literature rather
 * than a gap in the catalogue.
 *
 * Verified against the archive on 2026-09-10 via `/api/earthquakes/map`:
 *
 *   agency id usp0006nn2  (USGS ComCat)
 *   origin    1994-11-14 19:15 UTC  =  1994-11-15 03:15 Philippine time
 *   magnitude Mw 7.1
 *   depth     31.5 km, quality 1 — measured
 *   epicentre 13.525 N, 121.067 E
 *
 * The stored instant matches the published local origin time of 03:15:30 PST on 15 November exactly,
 * which is worth noting: the archive's date appears to be the 14th because it stores UTC.
 *
 * External figures, each cited in `sources`:
 *
 *   Wikipedia / ADS   Mw 7.1; a 35 km ground rupture on the right-lateral Aglubang River Fault
 *   Field survey      largest runup 7.3 m on the south-western coast of Baco Island; 6.1 m on its
 *                     northern coastline; tsunami-affected area within about 10 km of the epicentre
 *   Frontiers (2022)  numerical modelling indicates the fault rupture alone is insufficient, and
 *                     proposes an earthquake-triggered submarine mass failure as a contributing
 *                     mechanism
 *
 * Casualty totals differ between published accounts. This story does not narrate them — the platform
 * holds no impact data — and the sources that do hold them are cited.
 */
const MINDORO_USGS_ID = 'usp0006nn2';

export const MINDORO_1994: Story = {
  id: 'mindoro-1994',
  title: 'A wave the earthquake does not explain',
  standfirst:
    'The 1994 Mindoro earthquake is well recorded: agreed magnitude, measured depth, a mapped '
    + 'thirty-five-kilometre rupture. The tsunami that followed was still larger than that rupture '
    + 'accounts for, and why is an open question.',
  place: 'Mindoro and Verde Island Passage',
  when: '15 November 1994',
  hazard: 'earthquake',
  shows: 'That a complete catalogue entry can still leave the mechanism unresolved.',
  beats: [
    {
      id: 'quarter-past-three',
      heading: 'A quarter past three in the morning',
      body: [
        'This archive stores the origin as 19:15 UTC on 14 November 1994. Locally that is 03:15 on the '
        + '15th, which is the date and time every published account uses.',
        'The reading is unusually clean for an event of this age: moment magnitude 7.1, depth 31.5 '
        + 'kilometres, and the depth is measured rather than assigned \u2014 unlike 43% of the '
        + 'readings in this archive.',
      ],
      camera: { latitude: 13.52, longitude: 121.07, zoom: 8.6 },
      focusEarthquakeAgencyId: MINDORO_USGS_ID,
      highlights: [{ latitude: 13.525, longitude: 121.067, label: 'Epicentre' }],
    },
    {
      id: 'the-rupture',
      heading: 'Thirty-five kilometres of the Aglubang River Fault',
      body: [
        'The earthquake was a right-lateral strike-slip rupture on the Aglubang River Fault, and it '
        + 'broke the surface for about thirty-five kilometres across northern Mindoro.',
        'As in 1990, the catalogue holds one point where the ground moved along a line. The difference '
        + 'here is what happened next.',
      ],
      camera: { latitude: 13.4, longitude: 121.15, zoom: 8.8 },
      focusEarthquakeAgencyId: MINDORO_USGS_ID,
      layerNames: ['Active Faults (GEM)'],
      highlights: [{ latitude: 13.525, longitude: 121.067, label: 'Epicentre' }],
    },
    {
      id: 'the-wave',
      heading: 'Seven metres at Baco Island',
      body: [
        'A post-event field survey measured a maximum runup of 7.3 metres on the south-western coast '
        + 'of Baco Island, and 6.1 metres on its northern shore. The affected area was tightly '
        + 'confined \u2014 within roughly ten kilometres of the epicentre.',
        'A strike-slip earthquake is a poor tsunami generator. It moves the ground sideways, and it is '
        + 'vertical displacement of the sea floor that lifts a water column. A seven-metre runup from '
        + 'a magnitude 7.1 strike-slip event is not what the mechanism predicts.',
      ],
      camera: { latitude: 13.58, longitude: 121.05, zoom: 10.2 },
      focusEarthquakeAgencyId: MINDORO_USGS_ID,
      highlights: [{ latitude: 13.53, longitude: 121.0, label: 'Baco \u00b7 runup to 7.3 m' }],
      caveat:
        'The runup figures are from a published field survey, not from this archive. Calametra holds '
        + 'no runup measurements, and the highlight marks the surveyed area rather than a gauge '
        + 'position.',
    },
    {
      id: 'the-open-question',
      heading: 'A second mechanism, proposed',
      body: [
        'Numerical modelling of the 1994 tsunami indicates that the fault rupture alone does not '
        + 'reproduce the observed waves. A 2022 study proposes an earthquake-triggered submarine mass '
        + 'failure \u2014 a landslide on the sea floor \u2014 as a contributing source, constrained by '
        + 'submarine geomorphology.',
        'That is a hypothesis under active investigation, not a settled fact, and this story presents '
        + 'it as one.',
        'What matters here is the shape of the problem. The earthquake catalogue for this event is as '
        + 'complete as this archive gets. It is still not sufficient to explain the hazard, because '
        + 'the relevant process is not an earthquake parameter at all.',
      ],
      camera: { latitude: 13.6, longitude: 121.0, zoom: 9.4 },
      focusEarthquakeAgencyId: MINDORO_USGS_ID,
      highlights: [{ latitude: 13.56, longitude: 121.02, label: 'Verde Island Passage' }],
    },
    {
      id: 'close',
      heading: 'What a complete entry still cannot do',
      body: [
        'This platform can tell you where this earthquake was, how large, how deep, and that the depth '
        + 'was genuinely measured. It can draw the fault system it occurred on.',
        'It cannot tell you why the water rose seven metres, because that answer lies in sea-floor '
        + 'geomorphology and tsunami modelling \u2014 neither of which is a hazard catalogue.',
        'A well-populated record is not the same as a sufficient one. Knowing which questions a dataset '
        + 'cannot answer is part of using it honestly.',
      ],
      camera: { latitude: 13.5, longitude: 121.1, zoom: 7.8 },
    },
  ],
  sources: [
    {
      title: 'Field survey of the 1994 Mindoro Island, Philippines tsunami',
      publisher: 'Natural Hazards (Springer)',
      url: 'https://link.springer.com/article/10.1007/BF00874399',
      note:
        'Largest recorded runup 7.3 m on the south-western coast of Baco Island and 6.1 m on its '
        + 'northern coastline; affected area within about 10 km of the epicentre.',
    },
    {
      title:
        'An earthquake-triggered submarine mass failure mechanism for the 1994 Mindoro tsunami in '
        + 'the Philippines',
      publisher: 'Frontiers in Earth Science',
      url: 'https://www.frontiersin.org/journals/earth-science/articles/10.3389/feart.2022.1067002/full',
      note:
        'Numerical modelling indicates the fault rupture alone is insufficient, and proposes a '
        + 'submarine mass failure as a contributing source. Also the source of the casualty totals '
        + 'this story deliberately does not narrate.',
    },
    {
      title: 'Source characterization of the 15 November 1994 Ms 7.1 Mindoro, Philippines earthquake',
      publisher: 'American Geophysical Union (via NASA ADS)',
      url: 'http://ui.adsabs.harvard.edu/abs/2004AGUFM.S11A1003S/abstract',
      note: 'Right-lateral strike-slip movement along the Aglubang River Fault.',
    },
    {
      title: '1994 Mindoro tsunami documentation',
      publisher: 'University of Washington tsunami archive',
      url: 'https://earthweb.ess.washington.edu/tsunami/specialized/events/mindoro/tsunami.html',
      note:
        'Contemporary documentation of the affected barangays and observed vertical run-up. Held '
        + 'here because Calametra does not reproduce impact records.',
    },
    {
      title: 'Earthquake catalogue (ComCat)',
      publisher: 'United States Geological Survey',
      url: 'https://earthquake.usgs.gov/earthquakes/search/',
      note: 'The reading this archive stores: Mw 7.1 at a measured depth of 31.5 km.',
    },
  ],
};
