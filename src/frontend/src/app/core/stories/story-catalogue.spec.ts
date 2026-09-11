import { describe, expect, it } from 'vitest';

import { STORIES } from './story-catalogue';

/**
 * Structural integrity of hand-written story content.
 *
 * These assertions are cheap and catch the failures this kind of content actually suffers: a
 * duplicated id that makes one story unreachable, a beat whose prose was left empty, a highlight
 * with no label, or a story shipped without attribution. None of them would throw at runtime — the
 * page would simply render something wrong or blank.
 *
 * They deliberately do not check the prose. Whether a figure in a sentence matches the archive is
 * verified by hand against the live API and recorded in each story's file header; a unit test cannot
 * confirm it without the database.
 */
describe('story catalogue', () => {
  it('holds more than one story', () => {
    expect(STORIES.length).toBeGreaterThan(1);
  });

  it('gives every story a unique id', () => {
    const ids = STORIES.map((story) => story.id);

    expect(new Set(ids).size).toBe(ids.length);
  });

  it('gives every story attribution', () => {
    // The rule the content is written to: a historical narrative without sources is what this
    // platform exists to argue against, so a story with none is a defect rather than an omission.
    for (const story of STORIES) {
      expect(story.sources.length, story.id).toBeGreaterThan(0);

      for (const source of story.sources) {
        expect(source.url, `${story.id} / ${source.title}`).toMatch(/^https?:\/\//);
        expect(source.publisher.length, `${story.id} / ${source.title}`).toBeGreaterThan(0);
        expect(source.note.length, `${story.id} / ${source.title}`).toBeGreaterThan(0);
      }
    }
  });

  it('states what each story shows about the record', () => {
    for (const story of STORIES) {
      expect(story.shows.length, story.id).toBeGreaterThan(0);
      expect(story.standfirst.length, story.id).toBeGreaterThan(0);
      expect(story.place.length, story.id).toBeGreaterThan(0);
      expect(story.when.length, story.id).toBeGreaterThan(0);
    }
  });

  it('gives every beat a unique id, a heading and prose', () => {
    for (const story of STORIES) {
      const beatIds = story.beats.map((beat) => beat.id);

      expect(new Set(beatIds).size, story.id).toBe(beatIds.length);
      expect(story.beats.length, story.id).toBeGreaterThan(0);

      for (const beat of story.beats) {
        expect(beat.heading.length, `${story.id}/${beat.id}`).toBeGreaterThan(0);
        expect(beat.body.length, `${story.id}/${beat.id}`).toBeGreaterThan(0);

        for (const paragraph of beat.body) {
          expect(paragraph.trim().length, `${story.id}/${beat.id}`).toBeGreaterThan(0);
        }
      }
    }
  });

  it('labels every highlight and places it inside the region', () => {
    // An unlabelled ring is a gold circle with no explanation, and a highlight outside the
    // archipelago is a coordinate transposition — the most likely error when writing these by hand.
    for (const story of STORIES) {
      for (const beat of story.beats) {
        for (const highlight of beat.highlights ?? []) {
          expect(highlight.label.length, `${story.id}/${beat.id}`).toBeGreaterThan(0);
          expect(highlight.latitude, `${story.id}/${beat.id}`).toBeGreaterThan(0);
          expect(highlight.latitude, `${story.id}/${beat.id}`).toBeLessThan(25);
          expect(highlight.longitude, `${story.id}/${beat.id}`).toBeGreaterThan(110);
          expect(highlight.longitude, `${story.id}/${beat.id}`).toBeLessThan(135);
        }
      }
    }
  });

  it('keeps every camera inside the region it is meant to frame', () => {
    for (const story of STORIES) {
      for (const beat of story.beats) {
        if (beat.camera === undefined) {
          continue;
        }

        expect(beat.camera.latitude, `${story.id}/${beat.id}`).toBeGreaterThan(0);
        expect(beat.camera.latitude, `${story.id}/${beat.id}`).toBeLessThan(25);
        expect(beat.camera.longitude, `${story.id}/${beat.id}`).toBeGreaterThan(110);
        expect(beat.camera.longitude, `${story.id}/${beat.id}`).toBeLessThan(135);
        expect(beat.camera.zoom, `${story.id}/${beat.id}`).toBeGreaterThan(3);
        expect(beat.camera.zoom, `${story.id}/${beat.id}`).toBeLessThan(16);
      }
    }
  });

  it('never focuses an earthquake and a storm in the same beat', () => {
    // A beat describes one subject. Both set would mean the readings panel and the map were showing
    // two different things while the prose discussed one.
    for (const story of STORIES) {
      for (const beat of story.beats) {
        const both = beat.focusEarthquakeAgencyId !== undefined
          && beat.focusCycloneStormId !== undefined;

        expect(both, `${story.id}/${beat.id}`).toBe(false);
      }
    }
  });

  it('matches the focused subject to the story hazard', () => {
    for (const story of STORIES) {
      for (const beat of story.beats) {
        if (story.hazard === 'earthquake') {
          expect(beat.focusCycloneStormId, `${story.id}/${beat.id}`).toBeUndefined();
        } else {
          expect(beat.focusEarthquakeAgencyId, `${story.id}/${beat.id}`).toBeUndefined();
        }
      }
    }
  });

  /**
   * The regression guard for the defect this content was re-keyed to fix.
   *
   * Story content once referenced events by this platform's own `HazardEvent.Id`. Those are UUIDv7s
   * minted at insert, so every one changes when the archive is re-ingested — and the failure is
   * silent: the fetch 404s, the readings panel renders empty, and the prose goes on quoting figures
   * that nothing on screen supports. Nothing in the type system distinguishes one opaque string from
   * another, so the shape of the identifier is the only thing that can be asserted.
   */
  it('anchors every beat to an agency identifier, never to an internal id', () => {
    const internalId = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

    for (const story of STORIES) {
      for (const beat of story.beats) {
        for (const anchor of [beat.focusEarthquakeAgencyId, beat.focusCycloneStormId]) {
          if (anchor === undefined) {
            continue;
          }

          expect(anchor, `${story.id}/${beat.id} is not blank`).not.toBe('');
          expect(
            internalId.test(anchor),
            `${story.id}/${beat.id} uses the internal id ${anchor}, which changes on re-ingest — `
              + 'use the agency identifier instead (us20008ixa, iscgem913230, 2013306N07162)',
          ).toBe(false);
        }
      }
    }
  });

  /**
   * Every earthquake story must in fact focus its earthquake somewhere.
   *
   * A story whose prose quotes magnitudes but focuses nothing cannot be checked by the reader
   * against the archive, which is the rule the whole catalogue is written to.
   */
  it('focuses its subject at least once per story', () => {
    for (const story of STORIES) {
      const anchored = story.beats.some((beat) =>
        story.hazard === 'earthquake'
          ? beat.focusEarthquakeAgencyId !== undefined
          : beat.focusCycloneStormId !== undefined);

      expect(anchored, `${story.id} focuses its subject in no beat`).toBe(true);
    }
  });
});
