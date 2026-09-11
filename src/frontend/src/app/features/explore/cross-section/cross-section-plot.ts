import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';

import { CrossSectionStore } from '../../../core/cross-section/cross-section-store';
import type { SectionPreset } from '../../../core/cross-section/section-presets';
import type { CrossSectionPoint } from '../../../core/api/contracts';
import {
  depthColourForKilometres,
  markerRadiusForMagnitude,
} from '../../../core/visual/depth-scale';
import { Icon } from '../../../shared/ui/icon/icon';

/** A plotted hypocentre, in pixels. */
interface PlottedPoint {
  readonly point: CrossSectionPoint;
  readonly x: number;
  readonly y: number;
  readonly radius: number;
  readonly colour: string;
  /** Faded when the event sits far off the section line. */
  readonly opacity: number;
}

interface AxisTick {
  readonly position: number;
  readonly label: string;
}

/**
 * The depth cross-section: hypocentres plotted against distance along a line.
 *
 * ── Why this draws in pixels rather than a scaled viewBox ───────────────────
 * The obvious approach — a fixed viewBox stretched with `preserveAspectRatio="none"` —
 * is wrong here, and visibly so. Non-uniform scaling turns every hypocentre into an
 * ellipse: horizontal distance spans a few hundred kilometres while depth spans a few
 * hundred too, but the panel is far wider than it is tall, so the horizontal axis gets
 * stretched several times more than the vertical. `vector-effect: non-scaling-stroke`
 * rescues the strokes and hides half the problem, which is worse than not hiding it.
 *
 * So the container is measured and the plot is drawn in real pixels. Circles stay
 * circular, and the panel height is controlled directly instead of falling out of an
 * aspect ratio.
 *
 * ── Why hand-drawn SVG rather than a charting library ──────────────────────
 * The encoding is specific enough that a general-purpose chart would fight it: depth
 * runs downward from zero, radius carries magnitude to match the map, colour reuses the
 * map's depth ramp so the two views read as the same data, and unmeasured depths need a
 * hollow dashed treatment no charting library has an opinion about.
 */
@Component({
  selector: 'cal-cross-section-plot',
  standalone: true,
  imports: [Icon],
  templateUrl: './cross-section-plot.html',
  styleUrl: './cross-section-plot.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CrossSectionPlot {
  private readonly store = inject(CrossSectionStore);

  private readonly plotSurface = viewChild<ElementRef<HTMLElement>>('surface');

  /**
   * Room for the axes.
   *
   * `bottom` carries two rows — the tick numbers, then the axis caption below them, each
   * with clearance. Sizing it for one row made the caption collide with the numbers;
   * sizing it tightly for two left the caption's descenders touching the panel edge.
   */
  private static readonly Margin = { top: 16, right: 20, bottom: 48, left: 52 };

  /** Fixed. Tall enough to separate the depth bands, short enough to leave map visible. */
  private static readonly Height = 214;

  /** Measured from the container. The fallback only applies before first measurement. */
  private readonly width = signal(900);

  protected readonly profile = this.store.profile;
  protected readonly loading = this.store.loading;
  protected readonly phase = this.store.phase;
  protected readonly includeAssignedDepths = this.store.includeAssignedDepths;
  protected readonly hoveredEventId = this.store.hoveredEventId;
  protected readonly activePreset = this.store.activePreset;
  protected readonly presets = this.store.presets;

  protected readonly margin = CrossSectionPlot.Margin;
  protected readonly height = CrossSectionPlot.Height;
  protected readonly plotWidth = this.width.asReadonly();

  protected readonly viewBox = computed(() => `0 0 ${this.width()} ${CrossSectionPlot.Height}`);

  private readonly innerWidth = computed(() =>
    Math.max(1, this.width() - CrossSectionPlot.Margin.left - CrossSectionPlot.Margin.right),
  );

  private static readonly InnerHeight =
    CrossSectionPlot.Height - CrossSectionPlot.Margin.top - CrossSectionPlot.Margin.bottom;

  protected readonly axisBottom = CrossSectionPlot.Margin.top + CrossSectionPlot.InnerHeight;

  /** Baseline for the tick numbers, clear of the plot area. */
  protected readonly tickBaseline = CrossSectionPlot.Margin.top + CrossSectionPlot.InnerHeight + 15;

  /**
   * Baseline for the horizontal axis caption.
   *
   * Anchored to the axis rather than to the SVG's bottom edge. Measuring back from the
   * edge left only a couple of pixels of clearance, so the caption's descenders ran into
   * the panel's footer rule.
   */
  protected readonly captionBaseline =
    CrossSectionPlot.Margin.top + CrossSectionPlot.InnerHeight + 36;

  constructor() {
    // An effect, not afterNextRender. The measured element lives inside the template's
    // @if for a loaded profile, so at construction time it does not exist yet — a
    // one-shot afterNextRender found an empty viewChild, never attached the observer,
    // and left the plot frozen at its fallback width while the panel grew around it.
    //
    // viewChild returns a signal, so this re-runs when the element appears (and again if
    // it is destroyed and recreated by a redraw).
    effect((onCleanup) => {
      const element = this.plotSurface()?.nativeElement;

      if (!element) {
        return;
      }

      // ResizeObserver rather than a window resize listener: the panel's width also
      // changes when a side drawer opens or devtools closes, neither of which
      // necessarily fires a window resize.
      const observer = new ResizeObserver((entries) => {
        const measured = entries[0]?.contentRect.width ?? 0;

        if (measured > 0) {
          this.width.set(Math.round(measured));
        }
      });

      observer.observe(element);

      // Seed immediately: the observer's first callback is asynchronous, and without
      // this the plot renders once at the fallback width before correcting itself.
      const initial = element.getBoundingClientRect().width;

      if (initial > 0) {
        this.width.set(Math.round(initial));
      }

      onCleanup(() => observer.disconnect());
    });
  }

  protected readonly points = computed<readonly PlottedPoint[]>(() => {
    const profile = this.profile();

    if (!profile || profile.points.length === 0) {
      return [];
    }

    const { left, top } = CrossSectionPlot.Margin;
    const lengthKm = profile.lengthKm || 1;
    const maxDepthKm = profile.maxDepthKm || 1;
    const corridorKm = profile.corridorKm || 1;
    const innerWidth = this.innerWidth();

    return profile.points.map((point) => {
      // Magnitude to radius using the map's own scale, so an M6 reads the same size in
      // both views. Scaled down: the section is denser than the map.
      const radius = markerRadiusForMagnitude(point.magnitude) * 0.7;

      // Events near the line are opaque; those at the corridor's edge fade. Without
      // this, a ±50 km corridor implies every point sits exactly on the section.
      const offsetFraction = Math.min(1, point.offsetKm / corridorKm);

      return {
        point,
        x: left + (point.alongKm / lengthKm) * innerWidth,
        y: top + (point.depthKm / maxDepthKm) * CrossSectionPlot.InnerHeight,
        radius,
        colour: depthColourForKilometres(point.depthKm, point.depthMeasured),
        opacity: 0.85 - offsetFraction * 0.5,
      };
    });
  });

  /** Horizontal axis: distance along the section. */
  protected readonly distanceTicks = computed<readonly AxisTick[]>(() => {
    const profile = this.profile();

    if (!profile) {
      return [];
    }

    const lengthKm = profile.lengthKm || 1;
    const innerWidth = this.innerWidth();

    // Tick count from available width: a narrow panel gets fewer labels rather than
    // overlapping ones.
    const step = niceStep(lengthKm, Math.max(3, Math.floor(innerWidth / 110)));
    const ticks: AxisTick[] = [];

    for (let value = 0; value <= lengthKm + 0.001; value += step) {
      ticks.push({
        position: CrossSectionPlot.Margin.left + (value / lengthKm) * innerWidth,
        label: `${Math.round(value)}`,
      });
    }

    return ticks;
  });

  /** Vertical axis: depth, increasing downward. */
  protected readonly depthTicks = computed<readonly AxisTick[]>(() => {
    const profile = this.profile();

    if (!profile) {
      return [];
    }

    const maxDepthKm = profile.maxDepthKm || 1;
    const step = niceStep(maxDepthKm, 5);
    const ticks: AxisTick[] = [];

    for (let value = 0; value <= maxDepthKm + 0.001; value += step) {
      ticks.push({
        position:
          CrossSectionPlot.Margin.top + (value / maxDepthKm) * CrossSectionPlot.InnerHeight,
        label: `${Math.round(value)}`,
      });
    }

    return ticks;
  });

  /** Mid-height of the plot area, for anchoring the rotated depth caption. */
  protected readonly depthLabelY = computed(
    () => CrossSectionPlot.Margin.top + CrossSectionPlot.InnerHeight / 2,
  );

  protected readonly distanceLabelX = computed(
    () => CrossSectionPlot.Margin.left + this.innerWidth() / 2,
  );

  /**
   * How many plotted points carry a depth the agency assigned rather than measured.
   *
   * Only non-zero once the reader has opted in. Counted so the caption can say how much
   * of what they are looking at is convention rather than measurement.
   */
  protected readonly assignedShown = computed(
    () => this.profile()?.points.filter((point) => !point.depthMeasured).length ?? 0,
  );

  protected readonly excludedCount = computed(
    () => this.profile()?.excludedAssignedDepthCount ?? 0,
  );

  protected toggleAssignedDepths(): void {
    this.store.setIncludeAssignedDepths(!this.includeAssignedDepths());
  }

  protected applyPreset(preset: SectionPreset): void {
    this.store.applyPreset(preset);
  }

  protected onHover(point: PlottedPoint | null): void {
    this.store.setHoveredEvent(point?.point.eventId ?? null);
  }

  protected reset(): void {
    this.store.reset();
  }

  protected close(): void {
    this.store.close();
  }

  protected tooltip(point: CrossSectionPoint): string {
    const magnitude =
      point.magnitude === null
        ? 'magnitude not reported'
        : `M${point.magnitude} ${point.magnitudeScale}`;

    const depth = point.depthMeasured
      ? `${point.depthKm} km deep`
      : `${point.depthKm} km — depth assigned, not measured`;

    return `${magnitude} · ${depth} · ${point.offsetKm} km off section`;
  }
}

/**
 * A round step size near the requested division count.
 *
 * Axis labels at 0, 50, 100 read as measurements; labels at 0, 47, 94 read as arithmetic
 * and make the reader do work the axis exists to save them.
 */
function niceStep(range: number, targetDivisions: number): number {
  const rough = range / Math.max(1, targetDivisions);
  const magnitude = Math.pow(10, Math.floor(Math.log10(rough)));
  const normalised = rough / magnitude;

  const step = normalised >= 5 ? 5 : normalised >= 2 ? 2 : 1;

  return step * magnitude;
}
