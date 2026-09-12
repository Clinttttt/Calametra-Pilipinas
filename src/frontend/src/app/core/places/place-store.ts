import { Injectable, computed, inject, signal } from '@angular/core';
import { Subject, debounceTime, distinctUntilChanged, filter, switchMap } from 'rxjs';

import { CalametraApi } from '../api/calametra-api';
import type { EarthquakeSummary, PlaceContext, PlaceMatch } from '../api/contracts';
import { NotificationStore } from '../notifications/notification-store';

/** Radii offered. The server accepts up to 300 km; these are the ones a place question needs. */
export const PLACE_RADII = [10, 25, 50, 100] as const;

/**
 * How many events the list loads at a time.
 *
 * 50 rather than everything: a 100 km radius holds 2,612 events around Carmen in Surigao del Sur
 * alone, and rendering thousands of rows into a side panel costs more than the reader gains. The
 * total is always stated, so a short list never implies a short record.
 */
const PLACE_EVENT_PAGE_SIZE = 50;

/**
 * Owns the place search, the chosen place and the radius its context is read at.
 *
 * Two things here are deliberate and worth stating.
 *
 * **Typing is debounced and the responses are ordered.** "SURI" issues requests for "SUR" and
 * "SURI", and nothing guarantees they return in that order — `switchMap` cancels the
 * superseded one, which is the same defect the cyclone name search solved with a sequence
 * number. Debounced at 220 ms: fast enough to feel immediate, slow enough that a five-letter
 * town is one request rather than five.
 *
 * **The context is keyed on the PSGC code, not on the internal id.** Guardrail 8: ids are
 * minted at insert and change when the directory is re-imported, so a place a reader has
 * bookmarked or cited has to resolve through the agency's own identifier. Five places in the
 * directory carry no code — the two Maguindanao halves, Basilan, Cotabato City and the
 * Bangsamoro region — and those are searchable but cannot be opened, which the panel says
 * rather than failing silently.
 */
@Injectable({ providedIn: 'root' })
export class PlaceStore {
  private readonly api = inject(CalametraApi);
  private readonly notifications = inject(NotificationStore);

  private readonly terms = new Subject<string>();

  private readonly _open = signal(false);
  private readonly _term = signal('');
  private readonly _matches = signal<readonly PlaceMatch[]>([]);
  private readonly _searching = signal(false);
  private readonly _selected = signal<PlaceMatch | null>(null);
  private readonly _context = signal<PlaceContext | null>(null);
  private readonly _loadingContext = signal(false);
  private readonly _failed = signal(false);
  private readonly _radiusKm = signal<number>(25);
  private readonly _events = signal<readonly EarthquakeSummary[]>([]);
  private readonly _eventsTotal = signal(0);
  private readonly _eventsPage = signal(1);
  private readonly _loadingEvents = signal(false);

  readonly open = this._open.asReadonly();
  readonly term = this._term.asReadonly();
  readonly matches = this._matches.asReadonly();
  readonly searching = this._searching.asReadonly();
  readonly selected = this._selected.asReadonly();
  readonly context = this._context.asReadonly();
  readonly loadingContext = this._loadingContext.asReadonly();
  readonly failed = this._failed.asReadonly();
  readonly radiusKm = this._radiusKm.asReadonly();

  /**
   * Every catalogued earthquake in the radius, newest first.
   *
   * The context endpoint answers "what does this place's record look like" — counts, the strongest
   * reading per scale, the most recent one. It deliberately summarises. But a reader looking at
   * "443 within 50 km, 1913-2026" reasonably wants to see them, so the list comes from the ordinary
   * earthquake search with this place's centre and radius: one tested slice, already paged, already
   * carrying the depth-quality and scale qualifications every reading needs.
   */
  readonly events = this._events.asReadonly();

  /** How many the catalogue holds in this radius, not how many are loaded. */
  readonly eventsTotal = this._eventsTotal.asReadonly();

  readonly loadingEvents = this._loadingEvents.asReadonly();

  /** Whether more pages remain. */
  readonly hasMoreEvents = computed(() => this._events().length < this._eventsTotal());

  /** True once a term long enough to search has been typed. */
  readonly searched = computed(() => this._term().trim().length >= 2);

  constructor() {
    this.terms
      .pipe(
        debounceTime(220),
        distinctUntilChanged(),
        filter((term) => term.length >= 2),
        // switchMap, not mergeMap: an earlier, slower response for a shorter prefix must
        // not overwrite the results for what the reader has actually typed.
        switchMap((term) => this.api.searchPlaces(term, 20)),
      )
      .subscribe({
        next: (matches) => {
          this._matches.set(matches);
          this._searching.set(false);
        },
        error: () => {
          this._searching.set(false);
          this.notifications.error('Place search could not be completed.');
        },
      });
  }

  openPanel(): void {
    this._open.set(true);
  }

  /**
   * Closes the panel and clears the selection.
   *
   * The selection is cleared rather than remembered because it draws a radius ring on the
   * map: leaving the ring behind with no panel explaining it would be a mark with nothing to
   * account for it.
   */
  closePanel(): void {
    this._open.set(false);
    this.clearSelection();
  }

  search(term: string): void {
    this._term.set(term);

    const trimmed = term.trim();

    if (trimmed.length < 2) {
      this._matches.set([]);
      this._searching.set(false);

      return;
    }

    this._searching.set(true);
    this.terms.next(trimmed);
  }

  /** Chooses a place and loads its context. Places without a PSGC code cannot be opened. */
  select(place: PlaceMatch): void {
    this._selected.set(place);
    this._context.set(null);
    this._failed.set(false);
    this.resetEvents();

    if (place.psgcCode !== null) {
      this.loadContext(place.psgcCode);
    }

    // Keyed on the coordinate rather than the code, so the list works for the five places that
    // carry no PSGC code at all — Isabela City and the Maguindanao halves among them.
    this.loadEvents(place, 1);
  }

  clearSelection(): void {
    this._selected.set(null);
    this._context.set(null);
    this._failed.set(false);
    this.resetEvents();
  }

  setRadiusKm(km: number): void {
    if (km === this._radiusKm()) {
      return;
    }

    this._radiusKm.set(km);

    const place = this._selected();
    const code = place?.psgcCode;

    if (code) {
      this.loadContext(code);
    }

    if (place) {
      this.resetEvents();
      this.loadEvents(place, 1);
    }
  }

  /** Loads the next page of the list, appending to what is shown. */
  loadMoreEvents(): void {
    const place = this._selected();

    if (place && !this._loadingEvents() && this.hasMoreEvents()) {
      this.loadEvents(place, this._eventsPage() + 1);
    }
  }

  private resetEvents(): void {
    this._events.set([]);
    this._eventsTotal.set(0);
    this._eventsPage.set(1);
  }

  private loadEvents(place: PlaceMatch, page: number): void {
    this._loadingEvents.set(true);

    this.api
      .searchEarthquakes({
        centreLatitude: place.latitude,
        centreLongitude: place.longitude,
        radiusKm: this._radiusKm(),
        // Newest first: the record thickens towards the present, so the first page of a
        // chronological listing would otherwise be the sparsest part of the archive.
        page,
        pageSize: PLACE_EVENT_PAGE_SIZE,
      })
      .subscribe({
        next: (result) => {
          this._events.update((current) =>
            page === 1 ? result.items : [...current, ...result.items],
          );
          this._eventsTotal.set(result.totalCount);
          this._eventsPage.set(page);
          this._loadingEvents.set(false);
        },
        error: () => {
          this._loadingEvents.set(false);
          // Not surfaced as a second error toast: the context request covers the same place and the
          // interceptor has already reported whatever went wrong.
        },
      });
  }

  private loadContext(psgcCode: string): void {
    this._loadingContext.set(true);
    this._failed.set(false);

    this.api.getPlaceContext(psgcCode, this._radiusKm()).subscribe({
      next: (context) => {
        this._context.set(context);
        this._loadingContext.set(false);
      },
      error: () => {
        this._loadingContext.set(false);
        this._failed.set(true);
        this.notifications.error('This place could not be loaded.');
      },
    });
  }
}
