import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { vi } from 'vitest';

import {
  type EarthquakeDetail,
  type LguContainedEarthquakeMapData,
} from '../../../core/api/contracts';
import { LguEarthquakeBrowser } from './lgu-earthquake-browser';

const DATA: LguContainedEarthquakeMapData = {
  canonicalPsgcCode: '1606810000',
  name: 'Lanuza',
  level: 'Municipality',
  boundaryGeometryAreaSquareKm: 318.6,
  count: 2,
  points: [
    { i: 'older', y: 9.1, x: 126.1, m: 4.8, s: 'Ms', d: 33, q: 2, t: 1_183_766_400_000, n: false },
    { i: 'newer', y: 9.2, x: 126.2, m: 5.2, s: 'Mw', d: 18, q: 1, t: 1_552_348_800_000, n: false },
  ],
  countSemantics: 'test',
  boundary: { label: 'COD-AB', vintage: '2024-10-31', attribution: 'OCHA' },
};

describe('LguEarthquakeBrowser', () => {
  let fixture: ComponentFixture<LguEarthquakeBrowser>;
  let host: HTMLElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    await TestBed.compileComponents();
    fixture = TestBed.createComponent(LguEarthquakeBrowser);
    fixture.componentRef.setInput('lguName', 'Lanuza');
    fixture.componentRef.setInput('state', { status: 'ready', data: DATA });
    host = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
  });

  it('shows a compact newest-first contained list in a viewport-bounded shell', () => {
    expect(host.textContent).toContain('Earthquakes in Lanuza');
    expect(host.textContent).toContain('2 contained events');
    expect(host.querySelector('.focus-browser__body')).not.toBeNull();
    expect(host.querySelector('.focus-browser__footer')?.textContent).toContain('Exit LGU focus');

    const rows = [...host.querySelectorAll('.focus-event')];
    expect(rows).toHaveLength(2);
    expect(rows[0]?.textContent).toContain('Mw 5.2');
    expect(rows[1]?.textContent).toContain('Ms 4.8');
    expect(rows[1]?.textContent).toContain('assigned');
  });

  it('uses one detail state for list selection and parent-supplied map selection', async () => {
    const selected = vi.fn();
    fixture.componentInstance.eventSelected.subscribe(selected);
    host.querySelectorAll<HTMLButtonElement>('.focus-event')[0]!.click();
    expect(selected).toHaveBeenCalledWith(DATA.points[1]);

    // The same selected id input is what Explore changes after either a list-row or marker click.
    fixture.componentRef.setInput('selectedEventId', 'newer');
    fixture.componentRef.setInput('selectedDetail', detail('newer'));
    await fixture.whenStable();

    expect(host.querySelector('cal-event-detail')).not.toBeNull();
    expect(host.textContent).toContain('Back to 2 earthquakes');
    expect(host.querySelector('.focus-browser__list')).toBeNull();
  });

  it('returns from detail to the contained list', async () => {
    const backed = vi.fn();
    fixture.componentInstance.backRequested.subscribe(backed);
    fixture.componentRef.setInput('selectedEventId', 'newer');
    fixture.componentRef.setInput('selectedDetail', detail('newer'));
    await fixture.whenStable();

    host.querySelector<HTMLButtonElement>('.focus-browser__back')!.click();
    expect(backed).toHaveBeenCalledOnce();

    fixture.componentRef.setInput('selectedEventId', null);
    await fixture.whenStable();
    expect(host.querySelectorAll('.focus-event')).toHaveLength(2);
  });

  it('keeps zero, unavailable and failure as distinct states', async () => {
    fixture.componentRef.setInput('state', {
      status: 'ready',
      data: { ...DATA, count: 0, points: [] },
    });
    await fixture.whenStable();
    expect(host.textContent).toContain('No catalogue earthquakes fall within this land boundary.');

    fixture.componentRef.setInput('state', {
      status: 'unavailable',
      canonicalPsgcCode: DATA.canonicalPsgcCode,
    });
    await fixture.whenStable();
    expect(host.textContent).toContain('containment is unavailable');

    fixture.componentRef.setInput('state', {
      status: 'failed',
      canonicalPsgcCode: DATA.canonicalPsgcCode,
    });
    await fixture.whenStable();
    expect(host.textContent).toContain('normal archive is not being shown as a substitute');
  });
});

function detail(id: string): EarthquakeDetail {
  return {
    id,
    occurredAt: '2019-03-12T00:00:00Z',
    latitude: 9.2,
    longitude: 126.2,
    location: 'Lanuza, Surigao del Sur',
    observations: [],
    hasMagnitudeDisagreement: false,
    disagreementExplanation: null,
  };
}
