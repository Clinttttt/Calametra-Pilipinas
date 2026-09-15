# ADR-003 — PHIVOLCS hazard layers are proxied, not copied

**Status** Accepted · 2026-09-04
**Revisit when** PHIVOLCS responds to the formal data request

## Context

Calametra needs PHIVOLCS active fault geometry. The public ArcGIS endpoint at
`gisweb.phivolcs.dost.gov.ph/arcgis/rest/services/PHIVOLCSPublic` was probed on
2026-09-04. It exposes twelve hazard services: ActiveFault, Trenches, Liquefaction,
Tsunami, GroundShaking, EarthquakeInducedLandslide, Seiches, and five volcanic layers.
All are WGS 84.

What actually works, tested rather than read from documentation:

| Operation | Result |
|---|---|
| `/query` (all variants, incl. `returnCountOnly`, `objectIds`, `f=json`) | **HTTP 400** — "The requested capability is not supported" |
| `/generatekml` | **HTTP 400** |
| `/export` | 200, PNG |
| WMS `GetMap`, `CRS=EPSG:3857` | 200, `image/png` |
| WMS `GetFeatureInfo`, `INFO_FORMAT=application/geojson` | 200, GeoJSON |
| `/identify` | 200, **full vector geometry + attributes** |

Two things about that table matter more than the individual rows.

First, **the service metadata is unreliable in both directions.** The layer advertises
`capabilities: "Map,Query"`, `supportsAdvancedQueries: true` and
`supportedQueryFormats: "JSON, AMF, geoJSON"` — and every query is refused. Conversely the
WMS capabilities document advertises only `EPSG:4326` and `CRS:84`, yet serves EPSG:3857
correctly. Behaviour was established by probing, and must be.

Second, **the access pattern is a publishing decision.** Rendering is offered; bulk vector
extraction is not (`exportTilesAllowed: false`, query off, KML off). `/identify` returns
geometry because it is a click-inspection tool, not a bulk endpoint — a `1000`-feature
response for the CARAGA bounding box is it hitting `maxRecordCount`, not it offering a
download.

Public reachability is not a redistribution licence.

## Decision

**V1 proxies. It does not copy.**

- `HazardLayerDefinition.DeliveryMode = RemoteWms` — imagery forwarded from the
  publisher's own service at request time. No geometry stored.
- Clicking a fault issues a live `GetFeatureInfo` and shows the agency's attributes
  verbatim.
- `DataSource.IsRedistributable = false` for both PHIVOLCS sources.
- `HazardLayerDefinition.PromoteToLocalVector` **refuses** while that flag is false, so
  the licensing position is enforced by the model rather than remembered by a developer.

Proxying goes through our API rather than direct from the browser for three reasons: the
upstream sends no CORS headers; attribution and caching belong in one place; and a
`hazard-proxy` rate limit (60/min, versus 300/min for our own data) means a client
hammering Calametra cannot become Calametra hammering PHIVOLCS. Tiles are cached seven
days — fault traces change on a timescale of years.

**Send the formal request now, not at Phase 2.** Institutional requests take weeks and the
answer determines whether local spatial analysis against faults is possible at all. Ask
specifically about redistribution of derived layers inside a public academic application,
with attribution.

## Consequences

Good:
- V1 ships without waiting on correspondence.
- No licensing exposure.
- Faults render and are inspectable — the user-facing feature is complete.

Bad:
- No PostGIS spatial analysis against fault geometry: "distance to nearest active fault"
  is not answerable until the data is granted. This is the real cost.
- Layer display depends on a third-party service being up. Mitigated by caching, not
  solved.
- Raster overlays cannot be restyled to the design tokens; official hazard imagery appears
  in the agency's own colours. Arguably correct — restyling an authority's hazard map would
  misrepresent it.

## If permission is granted

Set `IsRedistributable = true`, call `PromoteToLocalVector`, import geometry into PostGIS,
and switch the layer's delivery mode. No client change: the hazard catalogue endpoint
already tells the client which mode each layer uses.
