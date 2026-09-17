import { HttpErrorResponse } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { Subject } from 'rxjs';

import {
  LguSelectionStore,
  type SelectedLgu,
} from '../../../core/administrative/lgu-selection-store';
import { CalametraApi } from '../../../core/api/calametra-api';
import {
  type AdministrativeUnit,
  type LguEarthquakeContainment,
} from '../../../core/api/contracts';
import { LguPanel } from './lgu-panel';

const CANTILAN: SelectedLgu = {
  psgc: '1606805000',
  name: 'Cantilan',
  kind: 'Municipality',
  boundaryGeometryAreaSquareKm: 327.1,
};

const CEBU_CITY: SelectedLgu = {
  psgc: '0730600000',
  name: 'City of Cebu',
  kind: 'City',
  boundaryGeometryAreaSquareKm: 315,
};

const LANUZA: SelectedLgu = {
  psgc: '1606810000',
  name: 'Lanuza',
  kind: 'Municipality',
  boundaryGeometryAreaSquareKm: 318.6,
};

function containment(
  earthquakeCount: number,
  canonicalPsgcCode = CANTILAN.psgc,
): LguEarthquakeContainment {
  const boundaryGeometryAreaSquareKm =
    canonicalPsgcCode === LANUZA.psgc ? LANUZA.boundaryGeometryAreaSquareKm : 327.1;

  return {
    canonicalPsgcCode,
    name: 'Cantilan',
    level: 'Municipality',
    boundaryGeometryAreaSquareKm,
    landAreaSquareKm: boundaryGeometryAreaSquareKm,
    earthquakeCount,
    // Deliberately arbitrary: the panel must consume the semantics, not branch on this diagnostic value.
    spatialPredicate: 'A_FUTURE_EQUIVALENT_PREDICATE',
    countSemantics:
      'Distinct earthquake events whose canonical epicentres are covered by the selected current COD-AB land boundary. Points exactly on the boundary are included.',
    boundary: {
      label: 'COD-AB Philippines administrative boundaries',
      vintage: '2024-10-31',
      attribution: 'United Nations OCHA',
    },
  };
}

function administrativeUnit(
  selected: SelectedLgu,
  officialSquareKm: number | null,
): AdministrativeUnit {
  return {
    canonicalPsgcCode: selected.psgc,
    name: selected.name,
    level: selected.kind,
    registerEdition: 'PSGC 2Q 2026 (PSA publication datafile)',
    officialLandArea:
      officialSquareKm === null
        ? null
        : {
            squareKm: officialSquareKm,
            basis: 'Unspecified',
            editionLabel: 'PSA 2024 POPCEN official land area / 2019 masterlist',
            referenceYear: 2019,
            matrixId: '1A6DLPD0',
            sourceUpdatedAt: '2026-08-07T01:00:00Z',
            provenanceLabel: 'PSA 2024 POPCEN / DENR-LMB 2019',
            attribution: 'Philippine Statistics Authority; Land Management Bureau',
          },
  };
}

class ApiStub {
  readonly requestedCodes: string[] = [];
  readonly responses = new Map<string, Subject<LguEarthquakeContainment>>();
  readonly administrativeRequestedCodes: string[] = [];
  readonly administrativeResponses = new Map<string, Subject<AdministrativeUnit>>();

  getLguEarthquakeContainment(code: string): Subject<LguEarthquakeContainment> {
    this.requestedCodes.push(code);
    const response = new Subject<LguEarthquakeContainment>();
    this.responses.set(code, response);
    return response;
  }

  getAdministrativeUnit(code: string): Subject<AdministrativeUnit> {
    this.administrativeRequestedCodes.push(code);
    const response = new Subject<AdministrativeUnit>();
    this.administrativeResponses.set(code, response);
    return response;
  }
}

describe('LguPanel earthquake containment', () => {
  let fixture: ComponentFixture<LguPanel>;
  let host: HTMLElement;
  let store: LguSelectionStore;
  let api: ApiStub;

  beforeEach(async () => {
    api = new ApiStub();

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        LguSelectionStore,
        { provide: CalametraApi, useValue: api },
      ],
    });

    await TestBed.compileComponents();
    store = TestBed.inject(LguSelectionStore);
    fixture = TestBed.createComponent(LguPanel);
    host = fixture.nativeElement as HTMLElement;
  });

  it('loads the selected LGU and keeps its boundary area visible with containment', async () => {
    store.select(CANTILAN);
    await fixture.whenStable();

    expect(api.requestedCodes).toEqual([CANTILAN.psgc]);
    expect(api.administrativeRequestedCodes).toEqual([CANTILAN.psgc]);
    expect(host.querySelector('.lgu__hazard-state')?.textContent).toContain(
      'Counting distinct earthquake epicentres',
    );
    api.responses.get(CANTILAN.psgc)!.next(containment(12));
    api.responses.get(CANTILAN.psgc)!.complete();
    await fixture.whenStable();

    expect(host.querySelector('.lgu__hazard-count')?.textContent?.trim()).toBe('12');
    expect(host.textContent).toContain('327.1 km');
    expect(host.textContent).toContain(
      'Distinct earthquake epicentres within/on the current land boundary',
    );
    expect(normalisedText(host.querySelector('.lgu__hazard-summary'))).toBe(
      'Counts distinct earthquake epicentres within or on this LGU\u2019s land boundary. Offshore events and duplicate agency reports are excluded.',
    );
    expect(host.textContent).toContain('COD-AB Philippines administrative boundaries');
    expect(host.textContent).toContain('2024-10-31');
    expect(host.textContent).not.toContain('Points exactly on the boundary are included');
    expect(host.textContent).not.toContain('A_FUTURE_EQUIVALENT_PREDICATE');
  });

  it('shows Lanuza official and boundary areas as distinct sourced values', async () => {
    store.select(LANUZA);
    await fixture.whenStable();

    api.administrativeResponses.get(LANUZA.psgc)!.next(administrativeUnit(LANUZA, 292.27));
    api.responses.get(LANUZA.psgc)!.next(containment(6, LANUZA.psgc));
    await fixture.whenStable();

    expect(normalisedText(host.querySelector('.lgu__facts'))).toContain(
      'Official land area 292.27 km²',
    );
    expect(normalisedText(host.querySelector('.lgu__identity-source'))).toContain(
      'Administrative identity · PSA PSGC, 2Q 2026',
    );
    expect(normalisedText(host.querySelector('.lgu__identity-source'))).toContain(
      'Official land area · PSA 2024 POPCEN / DENR-LMB 2019',
    );
    expect(normalisedText(host.querySelector('.lgu__hazard-source'))).toContain(
      'Boundary area · 318.6 km²',
    );
    expect(normalisedText(host.querySelector('.lgu__hazard-source'))).toContain(
      'Area · Computed from the mapped boundary',
    );
  });

  it('never falls back to polygon area when official area is missing', async () => {
    store.select(LANUZA);
    await fixture.whenStable();

    api.administrativeResponses.get(LANUZA.psgc)!.next(administrativeUnit(LANUZA, null));
    api.responses.get(LANUZA.psgc)!.next(containment(6, LANUZA.psgc));
    await fixture.whenStable();

    expect(normalisedText(host.querySelector('.lgu__facts'))).toContain(
      'Official land area Not available',
    );
    expect(normalisedText(host.querySelector('.lgu__hazard-source'))).toContain(
      'Boundary area · 318.6 km²',
    );
    expect(normalisedText(host.querySelector('.lgu__facts'))).not.toContain('318.6');
  });

  it('keeps full methodology collapsed by default and expands it accessibly', async () => {
    store.select(CANTILAN);
    await fixture.whenStable();
    api.responses.get(CANTILAN.psgc)!.next(containment(12));
    await fixture.whenStable();

    const toggle = host.querySelector<HTMLButtonElement>('.lgu__hazard-disclosure');

    expect(toggle?.textContent?.trim()).toBe('Read more');
    expect(toggle?.getAttribute('aria-expanded')).toBe('false');
    expect(toggle?.getAttribute('aria-controls')).toBe('lgu-earthquake-methodology');
    expect(host.querySelector('#lgu-earthquake-methodology')).toBeNull();
    expect(host.textContent).not.toContain('Points exactly on the boundary are included');

    toggle!.click();
    await fixture.whenStable();

    expect(toggle?.textContent?.trim()).toBe('Show less');
    expect(toggle?.getAttribute('aria-expanded')).toBe('true');
    expect(host.querySelector('#lgu-earthquake-methodology')?.textContent).toContain(
      'Points exactly on the boundary are included',
    );
    expect(host.querySelector('#lgu-earthquake-methodology')?.textContent).toContain(
      'United Nations OCHA',
    );
    expect(host.textContent).not.toContain('A_FUTURE_EQUIVALENT_PREDICATE');
  });

  it('collapses the methodology again when Show less is used', async () => {
    store.select(CANTILAN);
    await fixture.whenStable();
    api.responses.get(CANTILAN.psgc)!.next(containment(12));
    await fixture.whenStable();

    const toggle = host.querySelector<HTMLButtonElement>('.lgu__hazard-disclosure')!;
    toggle.click();
    await fixture.whenStable();
    toggle.click();
    await fixture.whenStable();

    expect(toggle.textContent?.trim()).toBe('Read more');
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(host.querySelector('#lgu-earthquake-methodology')).toBeNull();
    expect(host.textContent).not.toContain('Points exactly on the boundary are included');
  });

  it('keeps boundary clarification collapsed by default and toggles it accessibly', async () => {
    store.select(CANTILAN);
    await fixture.whenStable();

    expect(normalisedText(host.querySelector('.lgu__boundary-summary'))).toBe(
      'This outline represents the LGU\u2019s land area only; municipal waters are not included. Selecting it identifies an administrative unit, not a search radius. Distance-based results elsewhere use a representative point and answer a different spatial question.',
    );

    const toggle = host.querySelector<HTMLButtonElement>('.lgu__boundary-disclosure')!;
    expect(toggle.textContent?.trim()).toBe('Read more');
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(toggle.getAttribute('aria-controls')).toBe('lgu-boundary-methodology');
    expect(host.querySelector('#lgu-boundary-methodology')).toBeNull();
    expect(host.textContent).not.toContain('separate spatial concept');

    toggle.click();
    await fixture.whenStable();

    expect(toggle.textContent?.trim()).toBe('Show less');
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(host.querySelector('#lgu-boundary-methodology')?.textContent).toContain(
      'may extend up to 15',
    );
    expect(host.querySelector('#lgu-boundary-methodology')?.textContent).toContain(
      'does not calculate or change radius-based results',
    );

    toggle.click();
    await fixture.whenStable();

    expect(toggle.textContent?.trim()).toBe('Read more');
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(host.querySelector('#lgu-boundary-methodology')).toBeNull();
  });

  it('renders a successful zero distinctly from unavailable data', async () => {
    store.select(CANTILAN);
    await fixture.whenStable();
    api.responses.get(CANTILAN.psgc)!.next(containment(0));
    await fixture.whenStable();

    expect(host.querySelector('.lgu__hazard-count')?.textContent?.trim()).toBe('0');
    expect(host.textContent).toContain('No earthquake epicentres in the catalogue');
    expect(host.textContent).not.toContain('Boundary unavailable');
  });

  it('identifies a known LGU whose land boundary is unavailable', async () => {
    store.select(CANTILAN);
    await fixture.whenStable();
    api.responses.get(CANTILAN.psgc)!.error(
      new HttpErrorResponse({
        status: 404,
        error: { code: 'lgu_boundary.not_available' },
      }),
    );
    await fixture.whenStable();

    expect(host.textContent).toContain('Boundary unavailable');
    expect(host.textContent).toContain('known LGU has no current COD-AB land boundary');
    expect(host.querySelector('.lgu__hazard-count')).toBeNull();
  });

  it('renders request and server failures separately from boundary unavailability', async () => {
    store.select(CANTILAN);
    await fixture.whenStable();
    api.responses.get(CANTILAN.psgc)!.error(new HttpErrorResponse({ status: 500 }));
    await fixture.whenStable();

    expect(host.textContent).toContain('Earthquake containment could not be loaded');
    expect(host.textContent).not.toContain('Boundary unavailable');
  });

  it('cancels the prior request and fetches containment when selection changes', async () => {
    store.select(CANTILAN);
    await fixture.whenStable();
    const cantilanResponse = api.responses.get(CANTILAN.psgc)!;

    store.select(CEBU_CITY);
    await fixture.whenStable();

    expect(api.requestedCodes).toEqual([CANTILAN.psgc, CEBU_CITY.psgc]);
    expect(cantilanResponse.observed).toBe(false);

    api.responses.get(CEBU_CITY.psgc)!.next(containment(34, CEBU_CITY.psgc));
    await fixture.whenStable();

    expect(host.textContent).toContain('City of Cebu');
    expect(host.querySelector('.lgu__hazard-count')?.textContent?.trim()).toBe('34');
  });

  it('resets expanded methodology when another LGU is selected', async () => {
    store.select(CANTILAN);
    await fixture.whenStable();
    api.responses.get(CANTILAN.psgc)!.next(containment(12));
    await fixture.whenStable();

    host.querySelector<HTMLButtonElement>('.lgu__hazard-disclosure')!.click();
    host.querySelector<HTMLButtonElement>('.lgu__boundary-disclosure')!.click();
    await fixture.whenStable();
    expect(host.querySelector('#lgu-earthquake-methodology')).not.toBeNull();
    expect(host.querySelector('#lgu-boundary-methodology')).not.toBeNull();

    store.select(CEBU_CITY);
    await fixture.whenStable();
    api.responses.get(CEBU_CITY.psgc)!.next(containment(34, CEBU_CITY.psgc));
    await fixture.whenStable();

    const toggle = host.querySelector<HTMLButtonElement>('.lgu__hazard-disclosure');
    expect(toggle?.textContent?.trim()).toBe('Read more');
    expect(toggle?.getAttribute('aria-expanded')).toBe('false');
    expect(host.querySelector('#lgu-earthquake-methodology')).toBeNull();
    expect(host.querySelector('.lgu__boundary-disclosure')?.getAttribute('aria-expanded')).toBe(
      'false',
    );
    expect(host.querySelector('#lgu-boundary-methodology')).toBeNull();
    expect(host.textContent).not.toContain('A_FUTURE_EQUIVALENT_PREDICATE');
  });
});

function normalisedText(element: Element | null): string {
  return element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
}
