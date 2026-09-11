import type { Story } from '../story-model';

/**
 * Three storms called Megi.
 *
 * Chosen because it is the one story in this catalogue about the record's own machinery rather than
 * about the earth. A reader searching for a storm types a name; the archive holds three storms under
 * this one, and they are not remotely alike.
 *
 * It is also the story behind a design decision the reader would otherwise never see. Durable
 * references in this platform \u2014 curated content, citations, shared links \u2014 use the IBTrACS storm
 * identifier rather than a name, and this is why.
 *
 * ── Verified against the archive on 2026-09-10 ──────────────────────────────
 *
 *   SELECT min(external_storm_id), name, local_name, min(captured_at), max(captured_at),
 *          count(*), count(DISTINCT data_source_id), min(minimum_pressure_millibars),
 *          max(wind_speed_knots), bool_or(is_landfall)
 *   FROM hazard_events h JOIN cyclone_track_points c ON c.hazard_event_id = h.id
 *   WHERE h.name = 'MEGI' GROUP BY h.id, name, local_name;
 *
 *   sid              local   fixes  agencies  min mb  max kt  landfall  dates
 *   2010285N13145    Juan      374         4     885     160  yes       11\u201324 Oct 2010
 *   2016265N10150    (none)    275         5     933     120  yes       21\u201329 Sep 2016
 *   2022099N11128    (none)    131         5     995      40  yes        8\u201313 Apr 2022
 *
 * Other reused names in the same archive, from the same query pattern:
 *
 *   THELMA    1956 (935 mb, 150 kt) · 1973 (991) · 1977 (945) · 1987 (910, 130 kt) · 1991 = Uring
 *             (988 mb, 45 kt) \u2014 the remembered one is the weakest of the five
 *   MAWAR     2012 (944 mb) · 2017 (989 mb) · 2023 = Betty (891 mb)
 *   MERANTI   2010 (970 mb) · 2016 = Ferdie (890 mb)
 *   GONI      2015 (930 mb) · 2020 = Rolly (884 mb)
 *   ANGELA    1989 (920 mb) · 1992 (960 mb) · 1995 = Rosing (910 mb)
 *   BOPHA     2000 (988 mb) · 2006 (980 mb) · 2012 = Pablo (911 mb)
 *   IKE       1981 (965 mb) · 1984 = Nitang (945 mb)
 *   XANGSANE  2000 = Reming (960 mb) · 2006 = Milenyo (916 mb)
 *   DURIAN    2001 (965 mb) · 2006 = Reming (904 mb)
 *
 * The last two rows are the case that runs the other way: PAGASA assigned **Reming** to Xangsane in
 * 2000 and again to Durian in 2006, so the local name repeats as well and the mapping is one-to-many
 * in both directions. Verified in the archive after the crosswalk was extended.
 *
 * Note GONI and BOPHA: the earlier storms of each name carry no PAGASA name in this archive and the
 * later ones are Rolly and Pablo. Keyed on name alone, the curated mapping would have attached those
 * to every storm of the name. It is keyed on name *and* season precisely because of cases like this,
 * and the result above confirms it worked.
 *
 * ── External figures, each cited in `sources` ───────────────────────────────
 *
 *   WMO            maintains rotating lists of names per basin; a name is retired and replaced
 *                  only if the cyclone is "particularly deadly or costly"
 *   Typhoon        the published rotating list for the Western North Pacific and South China Sea,
 *   Committee      contributed by fourteen members
 *   PAGASA         four alphabetic sets of twenty-five names, rotating each year, with damaging
 *                  names replaced
 *
 * ⚠ Deliberately not claimed: the PAGASA names of the 2016 and 2022 storms. Every storm entering the
 * Philippine Area of Responsibility receives one, so their absence here is this archive's coverage
 * gap and not a fact about the storms \u2014 which the story states. The curated crosswalk holds 28
 * entries and no machine-readable international-to-local mapping is published, so naming them from
 * memory or from a secondary summary is exactly the invention this platform refuses.
 */

/** Typhoon Megi of October 2010, PAGASA's Juan. 885 mb. */
const MEGI_2010_SID = '2010285N13145';

/** Typhoon Megi of September 2016. 933 mb. */
const MEGI_2016_SID = '2016265N10150';

/** Tropical Storm Megi of April 2022. 995 mb. */
const MEGI_2022_SID = '2022099N11128';

export const STORM_NAMES: Story = {
  id: 'storm-names',
  title: 'Three storms, one name',
  standfirst:
    'This archive holds three tropical cyclones called Megi. One bottomed out at 885 millibars with '
    + 'winds of 160 knots. Another peaked at 40 knots. A name is not an identifier.',
  place: 'The Philippine Area of Responsibility',
  when: '2010, 2016 and 2022',
  hazard: 'cyclone',
  shows: 'Why durable references use the storm identifier and never the name.',
  beats: [
    {
      id: 'juan',
      heading: 'October 2010',
      body: [
        'The first Megi in this archive was tracked from 11 to 24 October 2010 and made landfall in '
        + 'the Philippines. Four agencies analysed it, between them filing 374 fixes. Its lowest '
        + 'central pressure was 885 millibars and its highest reported wind 160 knots.',
        'PAGASA called it Juan, and that is the name this archive carries for it \u2014 which for a '
        + 'Philippine reader is very likely the only name they would recognise.',
      ],
      camera: { latitude: 17.5, longitude: 122.0, zoom: 5.4 },
      focusCycloneStormId: MEGI_2010_SID,
    },
    {
      id: 'again',
      heading: 'Then again in 2016',
      body: [
        'Six years later another Megi was tracked from 21 to 29 September 2016, by five agencies, '
        + 'across 275 fixes. It also made landfall. Its lowest pressure was 933 millibars \u2014 a '
        + 'serious storm, and 48 millibars weaker than the first.',
        'It is a different storm in every respect except the label. Same name, different year, '
        + 'different track, different intensity.',
      ],
      camera: { latitude: 20.0, longitude: 122.5, zoom: 5 },
      focusCycloneStormId: MEGI_2016_SID,
    },
    {
      id: 'and-2022',
      heading: 'And a third, which was barely a storm',
      body: [
        'The third was tracked from 8 to 13 April 2022. Five agencies, 131 fixes, a minimum pressure '
        + 'of 995 millibars and a peak wind of 40 knots. By intensity it does not belong in the same '
        + 'conversation as the 2010 storm: 110 millibars separate them, and 120 knots.',
        'It made landfall too. A weak storm crossing land is not the same event as a strong one, and '
        + 'nothing about sharing a name makes them comparable.',
      ],
      camera: { latitude: 11.0, longitude: 125.0, zoom: 5.6 },
      focusCycloneStormId: MEGI_2022_SID,
    },
    {
      id: 'why-reuse',
      heading: 'Why names come round again',
      body: [
        'This is not an error in the record. The World Meteorological Organization maintains rotating '
        + 'lists of names for each cyclone basin, and the list for the Western North Pacific is '
        + 'contributed by fourteen members and cycled through repeatedly. A name leaves the list only '
        + 'when a storm bearing it is judged particularly deadly or costly, at which point it is '
        + 'retired and replaced.',
        'Megi stayed on the list. So it came back, twice.',
        'PAGASA runs its own system in parallel: four alphabetic sets of twenty-five names rotating '
        + 'each year, with damaging names likewise replaced. That is why one storm can carry two '
        + 'names, and why the archive stores both.',
      ],
      camera: { latitude: 14.0, longitude: 124.0, zoom: 4.6 },
      focusCycloneStormId: MEGI_2010_SID,
    },
    {
      id: 'not-just-megi',
      heading: 'It is not only Megi',
      body: [
        'Mawar appears in 2012, 2017 and 2023, the last of these as PAGASA\u2019s Betty. Meranti appears '
        + 'in 2010 and again in 2016, as Ferdie. Goni appears in 2015 and in 2020, as Rolly.',
        'That last pair is instructive. Rolly belongs to the 2020 storm only. A crosswalk keyed on the '
        + 'name alone would have attached it to the 2015 storm as well, quietly, and this platform '
        + 'would have shown a Philippine name for a storm that never bore it. The mapping is keyed on '
        + 'name and season together for that reason.',
      ],
      camera: { latitude: 14.5, longitude: 123.0, zoom: 4.4 },
    },
    {
      id: 'thelma',
      heading: 'Five storms called Thelma',
      body: [
        'Megi is not even the worst case. This archive holds five cyclones named Thelma \u2014 1956, '
        + '1973, 1977, 1987 and 1991 \u2014 and their minimum pressures run 935, 991, 945, 910 and 988 '
        + 'millibars.',
        'The one in Philippine public memory is the 1991 storm, PAGASA\u2019s Uring, and by both measures '
        + 'this archive holds it is the weakest of the five: 988 millibars and a peak wind of 45 '
        + 'knots. The 1987 Thelma reached 910 millibars and 130 knots, and almost nobody remembers it.',
        'Whatever makes a storm matter to a country, it is not the number this archive ranks them by. '
        + 'What happened in Ormoc in November 1991 is documented by the Philippine authorities cited '
        + 'below, and this platform holds none of it \u2014 no rainfall, no flood extent, no toll. It can '
        + 'show you that the storm was weak and it cannot show you why the name is remembered.',
      ],
      camera: { latitude: 11.0, longitude: 124.6, zoom: 6.2 },
      caveat:
        'Intensity is not impact. A platform that ranked storms only by pressure would put the 1991 '
        + 'Thelma near the bottom of its list.',
    },
    {
      id: 'both-directions',
      heading: 'And the local names repeat too',
      body: [
        'The reuse is easy to see one way round and easy to miss the other. PAGASA assigned the name '
        + 'Reming to Xangsane in 2000, and then again to Durian in 2006. Both storms are in this '
        + 'archive, and searching Reming returns both.',
        'So the mapping between international and Philippine names is not one-to-one in either '
        + 'direction. Two names, two rotating systems, two authorities, and neither of them is an '
        + 'identifier.',
      ],
      camera: { latitude: 13.5, longitude: 123.8, zoom: 5.2 },
    },
    {
      id: 'identifier',
      heading: 'What to reference instead',
      body: [
        'Every fix of the 2010 storm carries the identifier 2010285N13145: the season, the day of the '
        + 'year, and the position where the system was first detected. The 2022 storm is '
        + '2022099N11128. These are assigned by the compilers of the best-track archive, not by any '
        + 'one warning centre, and they do not repeat.',
        'So that is what this platform references when a reference has to last. Every story in this '
        + 'catalogue names its storms and earthquakes by the identifier its source agency published, '
        + 'never by name and never by Calametra\u2019s own internal key \u2014 which is regenerated each time '
        + 'the archive is rebuilt.',
      ],
      camera: { latitude: 12.5, longitude: 122.5, zoom: 4.6 },
      focusCycloneStormId: MEGI_2022_SID,
    },
    {
      id: 'close',
      heading: 'The gap this leaves',
      body: [
        'Two of these three storms carry no PAGASA name in this archive. That is a gap in Calametra, '
        + 'not a fact about the storms: every cyclone entering the Philippine Area of Responsibility '
        + 'is named by PAGASA. The crosswalk here is hand-transcribed and holds 28 entries, because no '
        + 'machine-readable mapping from international to local name is published.',
        'The panel says so wherever a local name is missing, rather than leaving an empty field for a '
        + 'reader to misread as "this storm had no Philippine name".',
      ],
      camera: { latitude: 13.0, longitude: 123.5, zoom: 4.4 },
      caveat:
        'Naming the missing two from a secondary summary would put this platform\u2019s authority behind '
        + 'an unverified claim. They stay blank, and the gap is stated.',
    },
  ],
  sources: [
    {
      title: 'Tropical cyclone naming',
      publisher: 'World Meteorological Organization',
      url: 'https://community.wmo.int/site/knowledge-hub/programmes-and-initiatives/tropical-cyclone-programme-tcp/tropical-cyclone-naming',
      note:
        'WMO maintains rotating lists of names per basin; a name is retired and replaced only if the '
        + 'cyclone is particularly deadly or costly.',
    },
    {
      title: 'Revised list of names for tropical cyclones, Western North Pacific and South China Sea',
      publisher: 'ESCAP/WMO Typhoon Committee',
      url: 'https://www.typhooncommittee.org/42nd/docs/others/Revised%20list%20of%20TC%20Names.pdf',
      note: 'The published rotating list from which Megi is drawn, contributed by fourteen members.',
    },
    {
      title: 'List of retired Philippine typhoon names',
      publisher: 'PAGASA naming practice, compiled reference',
      url: 'https://en.wikipedia.org/wiki/List_of_retired_Philippine_typhoon_names',
      note:
        'PAGASA uses four alphabetic sets of twenty-five names rotating each year, replacing names '
        + 'used by particularly damaging or deadly storms. Cited for the naming practice only.',
    },
    {
      title: 'International Best Track Archive for Climate Stewardship (IBTrACS)',
      publisher: 'NOAA National Centers for Environmental Information',
      url: 'https://www.ncei.noaa.gov/products/international-best-track-archive',
      note:
        'The source of the tracks and of the storm identifiers this story argues for. IBTrACS is a '
        + 'compilation: the wind values belong to the agencies that computed them.',
    },
    {
      title: 'Tropical Storm Thelma (Uring), November 1991',
      publisher: 'Compiled reference',
      url: 'https://en.wikipedia.org/wiki/Tropical_Storm_Thelma',
      note:
        'Cited for the identification of Thelma as PAGASA\u2019s Uring and for the impact record this '
        + 'platform does not hold. The intensity figures in the prose are from this archive, not '
        + 'from here.',
    },
    {
      title: 'Philippine tropical cyclone reports and disaster records',
      publisher: 'NDRRMC / PAGASA',
      url: 'https://www.pagasa.dost.gov.ph/climate/tropical-cyclone-information',
      note:
        'The authorities that hold rainfall, flood and casualty records for the storms named here. '
        + 'Calametra holds none of that and points at them instead of paraphrasing.',
    },
  ],
};
