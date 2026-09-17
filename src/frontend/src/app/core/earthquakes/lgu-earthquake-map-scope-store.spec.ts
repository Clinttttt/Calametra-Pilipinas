import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, provideZonelessChangeDetection } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { vi } from 'vitest';

import { LguSelectionStore, type SelectedLgu } from '../administrative/lgu-selection-store';
import { CalametraApi } from '../api/calametra-api';
import { type LguContainedEarthquakeMapData } from '../api/contracts';
import { PlaceStore } from '../places/place-store';
import { HazardModeStore } from '../hazards/hazard-mode-store';
import { LguEarthquakeMapScopeStore } from './lgu-earthquake-map-scope-store';
import { EarthquakeFilterStore } from './earthquake-filter-store';

const CANTILAN: SelectedLgu = {
  psgc: '1606805000',
  name: 'Cantilan',
  kind: 'Municipality',
  boundaryGeometryAreaSquareKm: 203.6,
};

const LANUZA: SelectedLgu = {
  psgc: '1606810000',
  name: 'Lanuza',
  kind: 'Municipality',
  boundaryGeometryAreaSquareKm: 318.6,
};

class ApiStub {
  readonly requests: string[] = [];
  readonly responses = new Map<string, Subject<LguContainedEarthquakeMapData>>();

  getLguContainedEarthquakeMapData(code: string): Subject<LguContainedEarthquakeMapData> {
    this.requests.push(code);
    const response = new Subject<LguContainedEarthquakeMapData>();
    this.responses.set(code, response);
    return response;
  }
}

@Component({ template: '' })
class ScopeHost {
  readonly scope = inject(LguEarthquakeMapScopeStore);
}

describe('LguEarthquakeMapScopeStore', () => {
  let fixture: ComponentFixture<ScopeHost>;
  let scope: LguEarthquakeMapScopeStore;
  let selection: LguSelectionStore;
  let api: ApiStub;
  let hazardMode: HazardModeStore;
  let filters: EarthquakeFilterStore;
  const place = {
    openPanel: vi.fn(),
    closePanel: vi.fn(),
    select: vi.fn(),
    setRadiusKm: vi.fn(),
  };

  beforeEach(async () => {
    api = new ApiStub();
    vi.clearAllMocks();

    TestBed.configureTestingModule({
      imports: [ScopeHost],
      providers: [
        provideZonelessChangeDetection(),
        LguSelectionStore,
        { provide: CalametraApi, useValue: api },
        { provide: PlaceStore, useValue: place },
      ],
    });

    await TestBed.compileComponents();
    fixture = TestBed.createComponent(ScopeHost);
    scope = fixture.componentInstance.scope;
    selection = TestBed.inject(LguSelectionStore);
    hazardMode = TestBed.inject(HazardModeStore);
    filters = TestBed.inject(EarthquakeFilterStore);
    await fixture.whenStable();
  });

  it('activates for the selected LGU without touching PlaceStore', async () => {
    filters.setMagnitudeRange(4.5, 7.2);
    filters.setDepthRange(10, 80);
    filters.setIncludeAssignedDepth(false);
    selection.select(CANTILAN);
    scope.activate();
    await fixture.whenStable();

    expect(api.requests).toEqual([CANTILAN.psgc]);
    expect(hazardMode.selected()).toBe('earthquakes');
    expect(scope.state()).toEqual({ status: 'loading', canonicalPsgcCode: CANTILAN.psgc });
    expect(place.openPanel).not.toHaveBeenCalled();
    expect(place.closePanel).not.toHaveBeenCalled();
    expect(place.select).not.toHaveBeenCalled();
    expect(place.setRadiusKm).not.toHaveBeenCalled();
    expect(filters.filter()).toEqual({
      fromMs: null,
      toMs: null,
      minMagnitude: 4.5,
      maxMagnitude: 7.2,
      minDepthKm: 10,
      maxDepthKm: 80,
      includeAssignedDepth: false,
    });
  });

  it('replaces the contained set when selection changes and ignores a stale response', async () => {
    selection.select(CANTILAN);
    scope.activate();
    await fixture.whenStable();
    const cantilan = api.responses.get(CANTILAN.psgc)!;

    selection.select(LANUZA);
    await fixture.whenStable();

    expect(cantilan.observed).toBe(false);
    expect(api.requests).toEqual([CANTILAN.psgc, LANUZA.psgc]);

    cantilan.next(response(CANTILAN, ['old']));
    api.responses.get(LANUZA.psgc)!.next(response(LANUZA, ['new']));
    await fixture.whenStable();

    const state = scope.state();
    expect(state.status).toBe('ready');
    if (state.status === 'ready') {
      expect(state.data.canonicalPsgcCode).toBe(LANUZA.psgc);
      expect(state.data.points.map((point) => point.i)).toEqual(['new']);
    }
  });

  it('keeps missing boundary distinct from an empty contained set', async () => {
    selection.select(CANTILAN);
    scope.activate();
    await fixture.whenStable();

    api.responses.get(CANTILAN.psgc)!.error(
      new HttpErrorResponse({ status: 404, error: { code: 'lgu_boundary.not_available' } }),
    );
    await fixture.whenStable();

    expect(scope.state()).toEqual({
      status: 'unavailable',
      canonicalPsgcCode: CANTILAN.psgc,
    });
  });

  it('clears only the containment scope and leaves the LGU selected', async () => {
    selection.select(CANTILAN);
    scope.activate();
    await fixture.whenStable();

    scope.clear();
    await fixture.whenStable();

    expect(scope.state()).toEqual({ status: 'inactive' });
    expect(selection.selectedPsgc()).toBe(CANTILAN.psgc);
  });
});

function response(lgu: SelectedLgu, ids: readonly string[]): LguContainedEarthquakeMapData {
  return {
    canonicalPsgcCode: lgu.psgc,
    name: lgu.name,
    level: lgu.kind,
    boundaryGeometryAreaSquareKm: lgu.boundaryGeometryAreaSquareKm,
    count: ids.length,
    points: ids.map((id) => ({
      i: id,
      y: 9,
      x: 126,
      m: 5,
      s: 'Mw',
      d: 10,
      q: 1,
      t: 0,
      n: false,
    })),
    countSemantics: 'test',
    boundary: { label: 'COD-AB', vintage: '2024-10-31', attribution: 'OCHA' },
  };
}
