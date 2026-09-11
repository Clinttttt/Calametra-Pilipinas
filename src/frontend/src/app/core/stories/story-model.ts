/**
 * STORY MODEL
 *
 * The shape of a guided reading. Content lives in `stories/`, one file per story, and the
 * catalogue in `story-catalogue.ts` — so adding a story is adding a file rather than editing a
 * growing module.
 *
 * ── The rule all story content follows ──────────────────────────────────────
 * Prose supplies context and questions. The archive supplies numbers. Any beat making a
 * quantitative claim about an event also focuses that event, so the reader can check the sentence
 * against the live data rather than taking it on trust — and so the narrative cannot drift from the
 * database as the archive is re-ingested.
 *
 * Every figure written into prose must be verified against the archive or against a cited external
 * authority, and the query or citation recorded beside it. Nothing is written from memory.
 *
 * ── How story content references events ─────────────────────────────────────
 * By the **agency's own identifier**, never by this platform's internal id. Internal ids are UUIDv7s
 * minted at insert and change on every re-ingest, so content keyed to one breaks silently the next
 * time the archive is rebuilt — the panel renders empty while the prose keeps asserting figures.
 * Agency identifiers (`us20008ixa`, `iscgem913230`, IBTrACS `2013306N07162`) survive re-ingestion
 * and are citable. See {@link StoryBeat.focusEarthquakeAgencyId}.
 *
 * ── What story content deliberately does not claim ──────────────────────────
 * No casualties, building damage, liquefaction or displacement figures in the prose. Calametra
 * holds no impact data, and narrating impacts would mean asserting facts the platform cannot show.
 * Where impact is central to why an event matters, the story says so plainly and points the reader
 * at the authority that holds those records through {@link StorySource}. That keeps the distinction
 * between "this platform establishes this" and "this is documented elsewhere" visible.
 */

/** Where the camera sits for a beat. */
export interface StoryCamera {
  readonly latitude: number;
  readonly longitude: number;
  readonly zoom: number;
  readonly pitch?: number;
  readonly bearing?: number;
}

/**
 * A place the prose is pointing at.
 *
 * Distinct from the camera, which frames a view, and from an epicentre, which is a measurement. A
 * highlight marks *the thing this paragraph is about* — a coastline that was inundated, a fault
 * segment that ruptured, a city that felt it — so the reader is not left scanning a wide frame for
 * whatever the sentence means.
 *
 * `radiusKm` draws the mark at true ground scale where the subject has an extent worth seeing. Left
 * null, the highlight is a fixed screen-sized ring, which claims a position and no extent.
 */
export interface StoryHighlight {
  readonly latitude: number;
  readonly longitude: number;
  /** Shown beside the ring. Kept short: it is a map label, not a caption. */
  readonly label: string;
  readonly radiusKm?: number;
}

/**
 * Where a claim in this story comes from.
 *
 * Required on every story, because a historical narrative without attribution is exactly the kind
 * of confident-sounding unsourced text this platform exists to argue against. `note` records what
 * the source was used for, so a reader can tell which sentence rests on which reference.
 */
export interface StorySource {
  readonly title: string;
  readonly publisher: string;
  readonly url: string;
  readonly note: string;
}

/**
 * One step of a story.
 *
 * A beat is both prose and a request to the platform: show this event, enable this layer, draw this
 * section, mark this place. That coupling is the point — the reader is looking at the real interface
 * with real data, not at illustrations of it.
 */
export interface StoryBeat {
  readonly id: string;
  readonly heading: string;
  /** Paragraphs, kept separate so the template does not parse markup. */
  readonly body: readonly string[];
  readonly camera?: StoryCamera;
  /**
   * Opens the multi-agency reading panel for this earthquake, by the **reporting agency's own**
   * identifier — `us20008ixa` (USGS ComCat), `iscgem913230` (ISC-GEM), `2017_0210_1403`
   * (PHIVOLCS bulletin).
   *
   * Deliberately not this platform's own event id. Every `HazardEvent.Id` is a UUIDv7 minted at
   * insert, so it is stable only for the lifetime of one database: re-ingest the archive and every
   * internal id changes. Story content referencing one would silently lose its event — the fetch
   * 404s, the readings panel renders empty, and nothing reports an error. The prose would still
   * claim figures the interface could no longer show, which is the precise failure this platform
   * exists to argue against.
   *
   * An agency identifier survives re-ingestion, is citable in a publication, and resolves to the
   * same event in anyone else's copy of the catalogue. Resolved through
   * `GET /api/earthquakes/external/{id}`.
   */
  readonly focusEarthquakeAgencyId?: string;
  /**
   * Draws every agency's track for this storm, by IBTrACS **SID** — `2013306N07162` for Haiyan.
   *
   * A separate field from {@link focusEarthquakeAgencyId} rather than one polymorphic "focus",
   * because a storm and an earthquake are different kinds of thing with different geometry and
   * different readings — the same distinction the rest of the platform maintains. A beat
   * referencing both would be describing two subjects at once.
   *
   * The SID rather than name and season because international names are reused: this archive holds
   * MERANTI in 2010 and 2016, GONI in 2015 and 2020, MAWAR in 2012, 2017 and 2023. Resolved
   * through `GET /api/cyclones/external/{sid}`.
   */
  readonly focusCycloneStormId?: string;
  /** Places this beat is pointing at, drawn in the story highlight colour. */
  readonly highlights?: readonly StoryHighlight[];
  /**
   * Hazard layers to switch on, matched by display name.
   *
   * By name rather than by id because layer ids are generated when reference data is seeded, so
   * they differ between databases and cannot be written into content. The names are seeded values
   * and therefore stable; a rename would need reflecting here, which is why the store logs a miss
   * rather than failing silently.
   */
  readonly layerNames?: readonly string[];
  /** Draws one of the verified cross-section profiles, by preset id. */
  readonly crossSectionPresetId?: string;
  /** A caveat that must travel with this beat, rendered in the caution treatment. */
  readonly caveat?: string;
}

/** Which hazard a story is about, so the index can group and label them. */
export type StoryHazard = 'earthquake' | 'cyclone';

export interface Story {
  readonly id: string;
  readonly title: string;
  readonly standfirst: string;
  /** Where the story is set, for the index card. */
  readonly place: string;
  readonly when: string;
  readonly hazard: StoryHazard;
  /**
   * What this story demonstrates about the record, in a few words.
   *
   * Present because the stories are not interchangeable: each was chosen for a specific property of
   * the archive it can show rather than assert, and the index should say which.
   */
  readonly shows: string;
  readonly beats: readonly StoryBeat[];
  readonly sources: readonly StorySource[];
}
