import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';

import { ComparisonStore } from '../../../core/comparison/comparison-store';
import type { EarthquakeDetail, ObservationSummary } from '../../../core/api/contracts';
import { Icon } from '../../../shared/ui/icon/icon';

/** One row of the attribute table: a label and the two sides' values. */
interface ComparisonRow {
  readonly label: string;
  readonly left: string;
  readonly right: string;
  /** True when the two values differ, so the row can be marked. */
  readonly differs: boolean;
}

/**
 * Two earthquakes set against each other.
 *
 * Laid out as an attribute table with the reference on the left, because a comparison is
 * read across rows rather than down columns — the eye needs the same attribute adjacent on
 * both sides.
 *
 * The three derived differences carry the server's own notes rather than wording composed
 * here. Whether two magnitudes can be differenced at all is a domain rule about scale
 * families, and the explanation has to come from the same place as the decision.
 */
@Component({
  selector: 'cal-event-comparison',
  standalone: true,
  imports: [Icon],
  templateUrl: './event-comparison.html',
  styleUrl: './event-comparison.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EventComparison {
  private readonly store = inject(ComparisonStore);

  protected readonly result = this.store.result;
  protected readonly loading = this.store.loading;

  /** Attributes shown for both events, in reading order. */
  protected readonly rows = computed<readonly ComparisonRow[]>(() => {
    const result = this.result();

    if (result === null) {
      return [];
    }

    const left = preferred(result.left);
    const right = preferred(result.right);

    return [
      row('When', formatDateTime(result.left.occurredAt), formatDateTime(result.right.occurredAt)),
      row('Magnitude', left?.magnitude?.display ?? 'Not reported', right?.magnitude?.display ?? 'Not reported'),
      // Scale is its own row rather than folded into the magnitude, because whether the
      // two share a family is the fact that decides if the numbers mean the same thing.
      row('Scale family', left?.magnitude?.scaleFamily ?? '—', right?.magnitude?.scaleFamily ?? '—'),
      row('Depth', left?.depth.display ?? 'Not reported', right?.depth.display ?? 'Not reported'),
      row(
        'Depth quality',
        left ? (left.depth.isMeasured ? 'Measured' : 'Agency default') : '—',
        right ? (right.depth.isMeasured ? 'Measured' : 'Agency default') : '—',
      ),
      row('Reported by', left?.agency ?? '—', right?.agency ?? '—'),
      row('Agency readings', `${result.left.observations.length}`, `${result.right.observations.length}`),
      // Above the coordinates, because two coordinate pairs do not tell a reader whether the
      // events were near each other and two place names do. Beyond 300 km from any city or
      // municipality the server sends nothing rather than naming a distant town.
      row(
        'Nearest place',
        result.left.location ?? 'None within 300 km',
        result.right.location ?? 'None within 300 km',
      ),
      row(
        'Position',
        formatPosition(result.left.latitude, result.left.longitude),
        formatPosition(result.right.latitude, result.right.longitude),
      ),
    ];
  });

  protected swap(): void {
    this.store.swap();
  }

  protected close(): void {
    this.store.close();
  }
}

function row(label: string, left: string, right: string): ComparisonRow {
  return { label, left, right, differs: left !== right };
}

/** The reading the platform treats as canonical for display, never an average. */
function preferred(detail: EarthquakeDetail): ObservationSummary | null {
  return detail.observations.find((observation) => observation.isPreferred)
    ?? detail.observations[0]
    ?? null;
}

function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString('en-PH', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

function formatPosition(latitude: number, longitude: number): string {
  return `${latitude.toFixed(3)}, ${longitude.toFixed(3)}`;
}
