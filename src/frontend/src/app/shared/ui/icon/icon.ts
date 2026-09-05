import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { ICON_PATHS, type IconName } from './icon-paths';

/**
 * Renders a glyph from the Calametra icon set.
 *
 * Accessibility: an icon is decorative by default and hidden from assistive
 * technology, because it almost always sits beside a text label. Passing a
 * `label` promotes it to an image with an accessible name — required whenever
 * the icon is the only content of a control, as in the lens rail.
 *
 * @example
 * ```html
 * <!-- Beside a visible label: hidden from screen readers -->
 * <cal-icon name="depth-section" />
 *
 * <!-- Sole content of a button: named -->
 * <button class="c-icon-button" type="button">
 *   <cal-icon name="lens-seismic" label="Seismic lens" />
 * </button>
 * ```
 */
@Component({
  selector: 'cal-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'cal-icon',
    '[style.--cal-icon-size.px]': 'size()',
  },
  template: `
    <svg
      [attr.width]="size()"
      [attr.height]="size()"
      [attr.aria-hidden]="label() ? null : 'true'"
      [attr.role]="label() ? 'img' : null"
      [attr.aria-label]="label()"
      [attr.stroke-width]="strokeWidth()"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-linecap="round"
      stroke-linejoin="round"
    >
      @for (path of paths(); track $index) {
        <path [attr.d]="path" />
      }
    </svg>
  `,
  styles: `
    .cal-icon {
      display: inline-grid;
      place-items: center;
      flex: none;
      color: inherit;
    }
  `,
})
export class Icon {
  /** Which glyph to draw. */
  readonly name = input.required<IconName>();

  /** Rendered edge length in pixels. */
  readonly size = input(20);

  /**
   * Accessible name. Omit when the icon accompanies visible text; supply it
   * when the icon is the only content of an interactive control.
   */
  readonly label = input<string | null>(null);

  /**
   * Stroke weight. Scaled up slightly at small sizes so glyphs do not fade
   * out, and down at large sizes so they do not look inflated — optical
   * compensation the viewBox alone cannot provide.
   */
  protected readonly strokeWidth = computed(() => {
    const size = this.size();

    if (size <= 16) {
      return 1.65;
    }

    return size >= 32 ? 1.35 : 1.5;
  });

  protected readonly paths = computed(() => ICON_PATHS[this.name()]);
}
