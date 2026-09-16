import { beforeEach, describe, expect, it } from 'vitest';

import { LguSelectionStore, type SelectedLgu } from './lgu-selection-store';

const adams: SelectedLgu = {
  psgc: '0102801000',
  name: 'Adams',
  kind: 'Municipality',
  areaSquareKm: 159.3,
};

const cebu: SelectedLgu = {
  psgc: '0730600000',
  name: 'City of Cebu',
  kind: 'City',
  areaSquareKm: 326.2,
};

describe('LguSelectionStore', () => {
  let store: LguSelectionStore;

  beforeEach(() => {
    // Constructed directly, as the other stores' tests do: a signal store has no injected
    // dependencies, so a test environment would add ceremony without adding coverage.
    store = new LguSelectionStore();
  });

  it('starts with nothing selected or hovered', () => {
    expect(store.selected()).toBeNull();
    expect(store.hoveredPsgc()).toBeNull();
    expect(store.hasSelection()).toBe(false);
  });

  it('keeps the canonical PSGC as the identity', () => {
    store.select(adams);

    // The leading zero matters: it is what distinguishes a Luzon code, and the integer form in the tile
    // drops it. Anything durable is keyed to the string.
    expect(store.selectedPsgc()).toBe('0102801000');
  });

  it('carries the area, because a figure from a boundary must state it', () => {
    store.select(adams);

    expect(store.selected()?.areaSquareKm).toBe(159.3);
  });

  it('replaces the selection rather than accumulating', () => {
    store.select(adams);
    store.select(cebu);

    expect(store.selected()?.name).toBe('City of Cebu');
  });

  it('holds the selection when the pointer leaves the outline', () => {
    // The behaviour a reader depends on: moving the pointer away is not dismissing the panel.
    store.select(adams);
    store.hover('0102801000');

    store.clearHover();

    expect(store.hoveredPsgc()).toBeNull();
    expect(store.selected()?.psgc).toBe('0102801000');
    expect(store.hasSelection()).toBe(true);
  });

  it('clears hover when the selection is dismissed', () => {
    // Otherwise a hover highlight survives with nothing selected, which reads as a half-applied state.
    store.select(adams);
    store.hover('0102801000');

    store.clear();

    expect(store.selected()).toBeNull();
    expect(store.hoveredPsgc()).toBeNull();
  });

  it('tracks hover independently of selection', () => {
    store.select(adams);
    store.hover('0730600000');

    expect(store.selectedPsgc()).toBe('0102801000');
    expect(store.hoveredPsgc()).toBe('0730600000');
  });

  it('keeps selectedPsgc consistent with selected', () => {
    expect(store.selectedPsgc()).toBeNull();

    store.select(cebu);

    expect(store.selectedPsgc()).toBe(store.selected()?.psgc);
  });
});
