import { describe, expect, it } from 'vitest';

import { radiusRing } from './radius-ring';

/**
 * Independent haversine, written here rather than imported.
 *
 * The point of these tests is that the ring's vertices really are the stated distance from the
 * centre. Measuring them with the same formula that generated them would only prove the code
 * agrees with itself, so this is the reverse calculation.
 */
function haversineKm(
  fromLat: number,
  fromLon: number,
  toLat: number,
  toLon: number,
): number {
  const radius = 6371.0088;
  const toRad = Math.PI / 180;

  const dLat = (toLat - fromLat) * toRad;
  const dLon = (toLon - fromLon) * toRad;

  const a =
    Math.sin(dLat / 2) ** 2 +
    Math.cos(fromLat * toRad) * Math.cos(toLat * toRad) * Math.sin(dLon / 2) ** 2;

  return 2 * radius * Math.asin(Math.sqrt(a));
}

describe('radiusRing', () => {
  it('closes the ring', () => {
    const ring = radiusRing(14.6, 121, 25).geometry.coordinates[0];

    expect(ring[0]).toEqual(ring[ring.length - 1]);
  });

  it('places every vertex at the stated radius', () => {
    // Surigao City, the platform's reference location.
    const ring = radiusRing(9.75, 125.5, 50).geometry.coordinates[0];

    for (const [longitude, latitude] of ring) {
      expect(haversineKm(9.75, 125.5, latitude, longitude)).toBeCloseTo(50, 2);
    }
  });

  it('holds the radius at the latitudes the archipelago actually spans', () => {
    // Batanes to Tawi-Tawi. A degree of longitude is 6% shorter at Batanes than at Tawi-Tawi,
    // which is exactly the error an offset in degrees would introduce — and it would appear at
    // the northern end, where the country's smallest province is.
    for (const latitude of [20.45, 14.6, 9.75, 5.05]) {
      const ring = radiusRing(latitude, 121, 100).geometry.coordinates[0];

      for (const [longitude, vertexLatitude] of ring) {
        expect(haversineKm(latitude, 121, vertexLatitude, longitude)).toBeCloseTo(100, 2);
      }
    }
  });

  it('widens in longitude towards the pole for the same ground radius', () => {
    // The consequence of the above, asserted directly: a 100 km circle spans more degrees of
    // longitude at Batanes than at Tawi-Tawi. A ring built by adding degrees would span the
    // same width at both, which is the bug this guards.
    const spread = (latitude: number): number => {
      const ring = radiusRing(latitude, 121, 100).geometry.coordinates[0];
      const longitudes = ring.map(([longitude]) => longitude);

      return Math.max(...longitudes) - Math.min(...longitudes);
    };

    expect(spread(20.45)).toBeGreaterThan(spread(5.05));
  });

  it('keeps longitudes inside the projection', () => {
    // 300 km is the server's cap. Around Batanes that reaches well east of the archipelago.
    const ring = radiusRing(20.45, 121.97, 300).geometry.coordinates[0];

    for (const [longitude] of ring) {
      expect(longitude).toBeGreaterThanOrEqual(-180);
      expect(longitude).toBeLessThanOrEqual(180);
    }
  });
});
