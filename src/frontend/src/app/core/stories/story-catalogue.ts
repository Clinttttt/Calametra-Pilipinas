import type { Story } from './story-model';
import { SURIGAO_2017 } from './stories/surigao-2017';
import { MORO_GULF_1976 } from './stories/moro-gulf-1976';
import { LUZON_1990 } from './stories/luzon-1990';
import { MINDORO_1994 } from './stories/mindoro-1994';
import { BOHOL_2013 } from './stories/bohol-2013';
import { HAIYAN_2013 } from './stories/haiyan-2013';
import { RECORD_COMPLETENESS } from './stories/record-completeness';
import { COTABATO_2019 } from './stories/cotabato-2019';
import { DEEP_FOCUS } from './stories/deep-focus';
import { STORM_NAMES } from './stories/storm-names';

/**
 * STORY CATALOGUE
 *
 * One file per story under `stories/`, listed here. Adding a story is adding a file and one line,
 * which is the point of the split: the previous single module mixed the model, the content and the
 * catalogue, so every new story enlarged a file three concerns already shared.
 *
 * ── Order ───────────────────────────────────────────────────────────────────
 * Not chronological. `record-completeness` leads because it explains how to read everything else —
 * it is the story that says the archive is a record of monitoring as much as of seismicity. The
 * event stories then run oldest to newest, and the two stories about the record's own structure —
 * depth, and identifiers — close the list, because both are easier to follow once a reader has seen
 * several individual events.
 *
 * ── What qualifies as a story here ──────────────────────────────────────────
 * Each one must show a property of the record that the platform would otherwise only assert, using
 * events the archive actually holds. That is a deliberately narrow test, and it is why there are ten
 * rather than fifty: a dramatic event with a single-agency reading and an assigned depth demonstrates
 * nothing the other stories do not, however significant it was.
 *
 * The corollary is that no two stories may teach the same lesson. Current coverage:
 *
 *   record-completeness   catalogue completeness changes with instrumentation, not seismicity
 *   moro-gulf-1976        an assigned depth, and published magnitudes that disagree
 *   luzon-1990            an epicentre is a computed point, not the location of an earthquake
 *   mindoro-1994          a complete entry can still be insufficient to explain what happened
 *   bohol-2013            the list of known faults is itself incomplete
 *   cotabato-2019         the record counts events; a place experiences a sequence
 *   haiyan-2013           wind averaging periods are not interchangeable; pressure nearly is
 *   surigao-2017          one earthquake has more than one correct magnitude
 *   deep-focus            a map is a plan projection, and depth is what it discards
 *   storm-names           a name is not an identifier
 *
 * Candidates deliberately not written, and why:
 *
 *   Samar 2012 (M7.6)        single agency, measured depth; the trench setting is covered by
 *                            Moro Gulf and it reveals nothing further about the record
 *   Mindanao 2023 (M7.6)     strong candidate for a sequence story, but Cotabato 2019 now covers
 *                            sequences, and the aftershock replay it would need is not built
 *   Mindanao 2026 (M7.8)     the largest event in the modern record and the obvious next story —
 *                            held back because at three months old its record is still provisional:
 *                            one agency, no transcribed national bulletin. That *is* the story to
 *                            write, once there is a second reading to set against the first
 *   Casiguran 1968 (M7.6)    the substance is its impact record, and this platform holds none
 *   Abra 2022 (M7.0)         well recorded and unremarkable in what it reveals about the data
 *   Odette / Rai 2021        five agencies and a severe track, but the multi-agency intensity
 *                            lesson is Haiyan's and this would repeat it
 */
export const STORIES: readonly Story[] = [
  RECORD_COMPLETENESS,
  MORO_GULF_1976,
  LUZON_1990,
  MINDORO_1994,
  BOHOL_2013,
  COTABATO_2019,
  HAIYAN_2013,
  SURIGAO_2017,
  DEEP_FOCUS,
  STORM_NAMES,
];
