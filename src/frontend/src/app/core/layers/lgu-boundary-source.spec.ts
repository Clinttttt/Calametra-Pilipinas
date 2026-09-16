import { describe, expect, it } from 'vitest';

import {
  LGU_ATTRIBUTION,
  LGU_MAX_ZOOM,
  LGU_MIN_ZOOM,
  LGU_SOURCE_LAYER,
  lguBoundarySource,
} from './lgu-boundary-source';

describe('lguBoundarySource', () => {
  it('addresses the platform’s own tile route', () => {
    const source = lguBoundarySource('https://api.example.test');

    expect(source.tiles[0]).toBe('https://api.example.test/api/lgu-boundaries/tile/{z}/{x}/{y}');
  });

  it('builds an absolute URL, because MapLibre never passes through the HTTP interceptor', () => {
    const source = lguBoundarySource('https://api.example.test');

    // A root-relative template would resolve against the document rather than the API, which is why
    // the hazard raster source takes a base URL for the same reason.
    expect(source.tiles[0].startsWith('https://')).toBe(true);
  });

  it('tolerates a trailing slash on the configured base URL', () => {
    const source = lguBoundarySource('https://api.example.test///');

    expect(source.tiles[0]).toBe('https://api.example.test/api/lgu-boundaries/tile/{z}/{x}/{y}');
  });

  it('declares a vector source', () => {
    expect(lguBoundarySource('https://api.example.test').type).toBe('vector');
  });

  it('does not request tiles at national zoom', () => {
    // ADR-005 D6 draws no municipality boundaries nationally, and a zoom-4 tile is 189 KB of outlines
    // nobody can see. The floor is part of the contract, not a preference.
    expect(LGU_MIN_ZOOM).toBe(6);
    expect(lguBoundarySource('https://api.example.test').minzoom).toBe(6);
  });

  it('stops building tiles where overzooming is free', () => {
    expect(LGU_MAX_ZOOM).toBe(14);
    expect(lguBoundarySource('https://api.example.test').maxzoom).toBe(14);
  });

  it('names the source layer the server actually writes', () => {
    // Getting this wrong produces an empty map and no error: the tiles arrive and nothing matches.
    expect(LGU_SOURCE_LAYER).toBe('lgu');
  });

  it('carries mandatory attribution naming the publishers rather than this platform', () => {
    const source = lguBoundarySource('https://api.example.test');

    expect(source.attribution).toBe(LGU_ATTRIBUTION);
    expect(source.attribution).toContain('OCHA');
    expect(source.attribution).toContain('NAMRIA');
    expect(source.attribution).toContain('CC BY 3.0 IGO');
  });

  it('states that the outlines are land, not municipal waters', () => {
    // The distinction ADR-005 D2a turns on. A reader who takes these for jurisdiction would read every
    // island municipality as thousands of square kilometres larger than it is.
    expect(LGU_ATTRIBUTION).toMatch(/not municipal waters/i);
  });
});
