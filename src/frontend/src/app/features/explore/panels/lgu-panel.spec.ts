import { HttpErrorResponse } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { Subject } from 'rxjs';

import {
  LguSelectionStore,
  type SelectedLgu,
} from '../../../core/administrative/lgu-selection-store';
import { CalametraApi } from '../../../core/api/calametra-api';
import { type LguEarthquakeContainment } from '../../../core/api/contracts';
import { LguPanel } from './lgu-panel';

const CANTILAN: SelectedLgu = {
  psgc: '1606801000',
  name: 'Cantilan',
  kind: 'Municipality',
  areaSquareKm: 327.1,
};

const CEBU_CITY: SelectedLgu = {
  psgc: '0730600000',
  name: 'City of Cebu',
  kind: 'City',
  areaSquareKm: 315,
};

function containment(
  earthquakeCount: number,
  canonicalPsgcCode = CANTILAN.psgc,
): LguEarthquakeContainment {
  return {
    canonicalPsgcCode,
    name: 'Cantilan',
    level: 'Municipality',
    landAreaSquareKm: 327.1,
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

class ApiStub {
  readonly requestedCodes: string[] = [];
  readonly responses = new Map<string, Subject<LguEarthquakeContainment>>();

  getLguEarthquakeContainment(code: string): Subject<LguEarthquakeContainment> {
    this.requestedCodes.push(code);
    const response = new Subject<LguEarthquakeContainment>();
    this.responses.set(code, response);
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

  it('loads the selected LGU and keeps its land area visible', async () => {
    store.select(CANTILAN);
    await fixture.whenStable();

    expect(api.requestedCodes).toEqual([CANTILAN.psgc]);
    expect(host.querySelector('.lgu__hazard-state')?.textContent).toContain(
      'Counting distinct earthquake epicentres',
    );
    expect(host.textContent).toContain('327.1 km');

    api.responses.get(CANTILAN.psgc)!.next(containment(12));
    api.responses.get(CANTILAN.psgc)!.complete();
    await fixture.whenStable();

    expect(host.querySelector('.lgu__hazard-count')?.textContent?.trim()).toBe('12');
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
