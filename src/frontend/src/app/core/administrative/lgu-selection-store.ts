import { Injectable, computed, signal } from '@angular/core';

/**
 * A municipality as the boundary tiles describe it.
 *
 * Deliberately only what the tile carries. Anything more is a question for the API, and conflating the
 * two would make the panel's contents depend on which tiles happened to be loaded.
 */
export interface SelectedLgu {
  /** Canonical ten-digit PSGC. The identity everything durable is keyed to. */
  readonly psgc: string;

  /** The register's authoritative name. */
  readonly name: string;

  /** City or Municipality, as the register classifies it. */
  readonly kind: string;

  /**
   * Area of the selected boundary geometry in square kilometres.
   *
   * Carried because ADR-005 D1 requires any figure derived from a boundary to state the unit's area
   * beside it: Philippine LGUs differ in area by more than two orders of magnitude, so a count inside
   * a boundary encodes polygon area unless the area is shown. The client cannot compute it from a
   * simplified tile, so the server sends it.
   */
  readonly boundaryGeometryAreaSquareKm: number;
}

/**
 * Which municipality the reader has selected, and which one they are pointing at.
 *
 * **A separate store from `PlaceStore`, deliberately.** ADR-005 D1 keeps containment and proximity as
 * two concepts: an LGU boundary answers *which unit is this in*, while the place directory's
 * representative point and radius answer *how much of the archive lies within this distance*. They are
 * not substitutes — Philippine LGUs differ in area by more than two orders of magnitude, so counts
 * inside boundaries compare polygon area as much as anything else, which is why the radius exists.
 *
 * Reusing `PlaceStore` would collapse that distinction in the one place it is most tempting to: a
 * municipality click would silently become a radius selection, and every figure downstream would then
 * be measured from a town centre while the reader believed they had asked about an administrative
 * unit. Two stores make the separation structural rather than remembered.
 *
 * Nothing here triggers a radius, moves the camera, or fetches place context. Selecting a municipality
 * selects a municipality.
 */
@Injectable({ providedIn: 'root' })
export class LguSelectionStore {
  private readonly selectedLgu = signal<SelectedLgu | null>(null);
  private readonly hoveredPsgcCode = signal<string | null>(null);

  /** The selected unit, or null. */
  readonly selected = this.selectedLgu.asReadonly();

  /** The unit under the pointer, or null. */
  readonly hoveredPsgc = this.hoveredPsgcCode.asReadonly();

  /**
   * The selected unit's code, for MapLibre filters.
   *
   * A computed rather than a second signal so it cannot drift from `selected`.
   */
  readonly selectedPsgc = computed(() => this.selectedLgu()?.psgc ?? null);

  /** Whether anything is selected, for template guards. */
  readonly hasSelection = computed(() => this.selectedLgu() !== null);

  select(lgu: SelectedLgu): void {
    this.selectedLgu.set(lgu);
  }

  /**
   * Clears the selection.
   *
   * Hover is cleared too. A selection dismissed while the pointer sits over its outline would otherwise
   * leave the hover highlight behind with nothing selected, which reads as a half-applied state.
   */
  clear(): void {
    this.selectedLgu.set(null);
    this.hoveredPsgcCode.set(null);
  }

  hover(psgc: string | null): void {
    this.hoveredPsgcCode.set(psgc);
  }

  /**
   * Clears hover only.
   *
   * Separate from {@link clear} because leaving the outline is not the same gesture as dismissing the
   * selection: a reader who has selected a municipality and then moves the pointer away still has it
   * selected, and the panel must not close under them.
   */
  clearHover(): void {
    this.hoveredPsgcCode.set(null);
  }
}
