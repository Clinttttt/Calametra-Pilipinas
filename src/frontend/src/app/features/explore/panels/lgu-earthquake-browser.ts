import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

import {
  DEPTH_QUALITY,
  type EarthquakeDetail,
  type MapPoint,
  type ObservationSummary,
} from '../../../core/api/contracts';
import { type LguEarthquakeMapScopeState } from '../../../core/earthquakes/lgu-earthquake-map-scope-store';
import { EventDetail } from './event-detail';

/** Compact master-detail rail for the temporary LGU earthquake focus context. */
@Component({
  selector: 'cal-lgu-earthquake-browser',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EventDetail],
  templateUrl: './lgu-earthquake-browser.html',
  styleUrl: './lgu-earthquake-browser.scss',
})
export class LguEarthquakeBrowser {
  readonly lguName = input.required<string>();
  readonly state = input.required<LguEarthquakeMapScopeState>();
  readonly selectedEventId = input<string | null>(null);
  readonly selectedDetail = input<EarthquakeDetail | null>(null);
  readonly detailLoading = input(false);
  readonly detailFailed = input(false);

  readonly eventSelected = output<MapPoint>();
  readonly backRequested = output<void>();
  readonly exitRequested = output<void>();
  readonly locateRequested = output<ObservationSummary>();

  protected readonly readyData = computed(() => {
    const state = this.state();
    return state.status === 'ready' ? state.data : null;
  });

  protected readonly events = computed(() =>
    [...(this.readyData()?.points ?? [])].sort((left, right) => right.t - left.t),
  );

  protected readonly showingDetail = computed(() => {
    const selected = this.selectedEventId();
    return selected !== null && this.events().some((event) => event.i === selected);
  });

  protected formatMagnitude(event: MapPoint): string {
    return event.m === null ? 'Magnitude unknown' : `${event.s} ${event.m.toFixed(1)}`;
  }

  protected formatDate(event: MapPoint): string {
    return new Date(event.t).toLocaleDateString('en-PH', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      timeZone: 'UTC',
    });
  }

  protected formatDepth(event: MapPoint): string {
    if (event.d === null) {
      return 'Depth unknown';
    }

    const qualifier = event.q === DEPTH_QUALITY.agencyAssigned ? ' · assigned' : '';
    return `${event.d.toFixed(1)} km${qualifier}`;
  }
}
