import { ChangeDetectionStrategy, Component, output } from '@angular/core';

import { DEPTH_BANDS, UNMEASURED_DEPTH_COLOUR, markerRadiusFor } from '../../../core/visual/depth-scale';
import { Icon } from '../../../shared/ui/icon/icon';

/**
 * Legend for the map's visual encoding.
 *
 * Opened from the tool rail rather than sitting permanently on the map. Depth is
 * encoded as hue and magnitude as size, and neither is guessable — but a legend that
 * is always present is chrome the user has to look past every time they use the map.
 * On demand is the right trade.
 *
 * The "not measured" entry carries equal weight to the depth bands deliberately.
 * 43% of the archive reports an agency-assigned depth, so it is one of the most
 * common things on screen, and a user who does not know what the grey markers mean
 * will misread the map.
 */
@Component({
  selector: 'cal-map-legend',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  templateUrl: './map-legend.html',
  styleUrl: './map-legend.scss',
})
export class MapLegend {
  readonly closed = output<void>();

  protected readonly depthBands = DEPTH_BANDS;
  protected readonly unmeasuredColour = UNMEASURED_DEPTH_COLOUR;
  protected readonly magnitudeStops = [4, 5, 6, 7, 8] as const;

  protected close(): void {
    this.closed.emit();
  }

  /** Legend dot diameter, sampled from the same function the map layer uses. */
  protected diameter(magnitude: number): number {
    return (
      markerRadiusFor({ value: magnitude, scale: 'Mw', scaleFamily: 'Moment', display: '' }) * 2
    );
  }
}
