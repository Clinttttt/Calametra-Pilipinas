import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

import { Icon } from '../../shared/ui/icon/icon';
import { LocatorMap, type LocatorEvent } from './locator-map';
import type {
  HazardFeatureCollection,
  NearbyCycloneTrack,
  PlaceContext,
  PlaceMatch,
} from '../../core/api/contracts';

/**
 * ONE SIDE OF THE COMPARISON: FIND A PLACE, THEN SHOW WHAT IT IS
 *
 * Identity before evidence. A reader has to know which two records they are reading before a number
 * means anything, so this card carries the name, the administrative hierarchy that disambiguates it,
 * the coordinates every radius is measured from, and a locator map — and only then does the page
 * start stating figures.
 *
 * <b>Why the hierarchy is not decoration.</b> 111 of the country's city and municipality names are
 * not unique. "Carmen" alone is four different municipalities; the province is what resolves it, and
 * a comparison built on the wrong Carmen is wrong without looking wrong.
 *
 * Extracted from the Compare page rather than inlined, for two reasons. It is used twice and would
 * otherwise be duplicated in the template, and it is genuinely a unit: a picker, a card and a figure
 * that only make sense together.
 */
@Component({
  selector: 'cal-subject-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, LocatorMap],
  templateUrl: './subject-picker.html',
  styleUrl: './subject-picker.scss',
})
export class SubjectPicker {
  /** `First place` or `Second place`. Labels the field, and is the card's accessible heading. */
  readonly heading = input.required<string>();

  /** Field id, so the label is programmatically associated rather than merely adjacent. */
  readonly fieldId = input.required<string>();

  readonly term = input('');
  readonly matches = input<readonly PlaceMatch[]>([]);
  readonly context = input<PlaceContext | null>(null);
  readonly loading = input(false);

  /** The M6.0+ events plotted on the map — the same series the page's headline count reports. */
  readonly events = input<readonly LocatorEvent[]>([]);
  readonly faults = input<HazardFeatureCollection | null>(null);

  /** Storm track segments near this place, already clipped by the API. */
  readonly tracks = input<readonly NearbyCycloneTrack[]>([]);

  /** Storms within the radius, which exceeds the tracks drawn when the API's limit bit. */
  readonly stormCount = input(0);

  /** Stated rather than assumed, so the legend and the page cannot disagree about the floor. */
  readonly comparableMagnitude = input.required<number>();

  /** How many passages the map emphasises, so the caption states the figure the map drew. */
  protected readonly emphasisedTracks = LocatorMap.emphasisedTracks;

  readonly termChange = output<string>();
  readonly chosen = output<PlaceMatch>();

  /** `City · Province of Surigao del Sur · Caraga`, skipping whichever parts are absent. */
  protected hierarchy(place: PlaceContext | PlaceMatch): string {
    return [place.kind, place.containedBy, place.region]
      .filter((part): part is string => part !== null && part.length > 0)
      .join(' · ');
  }

  /** Signed decimal degrees to four places: the form a professional reads and can re-enter. */
  protected coordinates(place: PlaceContext): string {
    return `${place.latitude.toFixed(4)}°N ${place.longitude.toFixed(4)}°E`;
  }

  /** The span the archive actually covers here, which is not the same as the archive's own span. */
  protected era(place: PlaceContext): string | null {
    if (place.earliestEvent === null || place.latestEvent === null) {
      return null;
    }

    return `${place.earliestEvent.slice(0, 4)}–${place.latestEvent.slice(0, 4)}`;
  }
}
