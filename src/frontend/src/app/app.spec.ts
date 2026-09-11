import { provideRouter } from '@angular/router';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { App } from './app';

/**
 * Shell smoke tests.
 *
 * Replaces the Angular scaffold spec, which asserted an `<h1>` reading "Hello, calametra" and had
 * been failing since the shell was rewritten. A test that cannot pass is worse than no test: it
 * makes a red suite the normal state, and a real regression then looks like the usual noise.
 *
 * These assert only what the shell is responsible for — the brand and the navigation rail — because
 * everything else belongs to the routed feature. The router must be provided explicitly: the shell
 * renders `routerLink` and `<router-outlet>`, so without it the component cannot be created at all,
 * which is what the first scaffold test was actually failing on.
 */
describe('App shell', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideZonelessChangeDetection(), provideRouter([])],
    }).compileComponents();
  });

  it('creates', () => {
    const fixture = TestBed.createComponent(App);

    expect(fixture.componentInstance).toBeTruthy();
  });

  it('renders the platform name', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('Calametra');
    expect(text).toContain('Pilipinas');
  });

  it('exposes a primary navigation landmark', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    const rail = (fixture.nativeElement as HTMLElement).querySelector('nav[aria-label="Primary"]');

    expect(rail).not.toBeNull();
  });
});
