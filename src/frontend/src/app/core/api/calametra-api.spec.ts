import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { CalametraApi } from './calametra-api';

describe('CalametraApi LGU earthquake containment', () => {
  let api: CalametraApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    api = TestBed.inject(CalametraApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('gets the hazard-specific endpoint for the canonical PSGC code', () => {
    api.getLguEarthquakeContainment('1606801000').subscribe();

    const request = http.expectOne('/api/lgu-boundaries/1606801000/earthquakes');

    expect(request.request.method).toBe('GET');
    request.flush({});
  });
});
