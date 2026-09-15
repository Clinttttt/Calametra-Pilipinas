# Calametra Pilipinas

**An Interactive Multi-Hazard Intelligence and Historical Exploration Web Platform**

National in scope: the Philippine archipelago.

**Not** a warning service. Official warnings come from PHIVOLCS, PAGASA and NDRRMC.

## Getting started

```powershell
# 1. Database (PostgreSQL 17 + PostGIS 3.5) — host port 5433, see note below
docker compose up -d database

# 2. Apply the schema
cd src/backend
dotnet ef database update --project src/Calametra.Infrastructure --startup-project src/Calametra.Api

# 3. Load the archive: seeds reference data, imports GEM faults, backfills USGS.
#    ~27,000 events for 1901-2026. Takes about seven minutes.
$env:DOTNET_ENVIRONMENT="Development"
dotnet run --project src/Calametra.Ingestion -- `
  --Ingestion:Backfill:From=1900-01-01 --Ingestion:Backfill:To=2026-09-01

# 4. Load the place directory: 1,750 regions, provinces, cities and municipalities.
#    A one-shot mode, like the cyclone import — administrative boundaries change by
#    legislation, not by the hour.
dotnet run --project src/Calametra.Ingestion -- --Ingestion:Places:Import=true
# 5. Put those places on their town centres. The gazetteer's own points are rounded to
#    the arc-minute for two thirds of the country and average 5.3 km from the poblacion,
#    which is the point every radius and distance is measured from.
dotnet run --project src/Calametra.Ingestion -- --Ingestion:Places:RefineCoordinates=true

# 6. Run the worker for ongoing ingestion (omit the backfill arguments)
dotnet run --project src/Calametra.Ingestion

# 7. API  (separate terminal)
cd src/backend
dotnet run --project src/Calametra.Api        # http://localhost:5130, docs at /scalar/v1

# 8. Frontend (separate terminal)
cd src/frontend
npm start                                     # http://localhost:4200
```

Run ingestion at least once before the API: nothing can be stored without the
`data_sources` rows it seeds, and the map is empty without events.

### Two things that will bite you

**The database is on host port 5433, not 5432.** A natively installed PostgreSQL service
commonly already owns 5432. When it does, Docker's mapping loses the race and
`localhost:5432` silently reaches the native server — and if that server happens to have a
matching role, the error is `28P01 password authentication failed` rather than anything
hinting that the wrong server answered.

**Set the environment variable for the worker.** `DOTNET_ENVIRONMENT` is unset in a plain
terminal, so the host resolves **Production** and reads `appsettings.json`, whose password
is a deliberate placeholder. The API is covered by its launch profile and EF commands by a
design-time factory; the worker is neither. Set `CALAMETRA_DB` to point EF at a different
database.

## Layout

```
src/backend/          .NET 10 — Clean Architecture + vertical slices
  src/
    Calametra.Domain           entities, value objects, invariants   (no project refs)
    Calametra.Application      slices, ports, dispatcher             (-> Domain)
    Calametra.Infrastructure   EF Core + PostGIS, source adapters    (-> Application)
    Calametra.Api              Minimal API host                      (-> App, Infra)
    Calametra.Ingestion        scheduled worker + backfill host       (-> App, Infra)
  tests/
    Calametra.Domain.UnitTests
    Calametra.Application.UnitTests           container wiring, request dispatch
    Calametra.Api.IntegrationTests            Testcontainers, real Postgres
    Calametra.ArchitectureTests               boundary + convention rules

src/frontend/         Angular 22 — standalone, signals, zoneless
  src/styles/                 design tokens, primitives
  src/app/core/               config, http interceptors, API client, visual encoding
  src/app/shared/ui/          hand-authored icon set
  src/app/features/           lazily loaded features

docs/adr/             architecture decision records
docs/DESIGN_SYSTEM.md design rules — read before adding UI
```

Two hosts is why this architecture is warranted rather than aspirational:
`Calametra.Ingestion` re-uses `Calametra.Application` without touching the HTTP path.

## Verify

```powershell
cd src/backend && dotnet build && dotnet test     # 179 tests
cd src/frontend && npm run build && npm test      # 77 tests
```

Confirmed against a live database and live upstream services on 2026-09-05:

| Check | Result |
|---|---|
| Schema | PostGIS 3.5.2, 7 tables, 6 `geography` columns, 6 GiST indexes |
| USGS backfill 1901–2026 | 27,241 events across 127 windows, zero failures |
| Agency-assigned depths | 11,790 (43.3%) — 33 km ×5,581, 10 km ×4,089, 35 km ×1,794, 15 km ×326 |
| Magnitude scales | `mb` dominant, `mww` / `mwc` / `mw` for large events, `ms` historical |
| Largest events present | 1918 Mindanao M8.3, 1924 M8.1, 1972 M8.0, 1976 Moro Gulf M7.9 |
| GEM fault import | 155 traces, incl. East Valley, Digdig, Casiguran, Lubang, 25 subduction segments |
| Nearest fault to the 2017 Surigao epicentre | Offshore Surigao 10.9 km, Surigao Fault 13.6 km |
| PHIVOLCS tile proxy | 200 `image/png`, cached 7 days |
| PHIVOLCS identify | Surigao-Sanghid Strand, Philippine Fault, mapped 2021 |
| PHIVOLCS hazard layers | Ground Shaking PEIS VIII at 1:50,000 in NCR; Liquefaction High Potential, GMMA-READY 2013 |
| Multi-source event | 2017 Surigao carries PHIVOLCS Ms 6.7 / 10 km and USGS Mww 6.5 / 15 km |
| Multi-agency readings | 6 events, incl. 2023 Hinatuan PHIVOLCS Mw 7.4 / 26 km against USGS Mww 7.6 / 40 km |
| Redistribution gate | GEM serves stored features; PHIVOLCS GIS serves 0 |
| Timeline | 1,497 monthly buckets, GPU-side filtering |

## Things that will surprise you

These are measured properties of the upstream data, not assumptions. Each is encoded in the
type system so it cannot be forgotten.

**The USGS catalogue holds effectively nothing below M4.0 anywhere in the Philippines.**
Measured across 2015-01-01 to 2026-09-01: 8,722 events at M0+, 8,722 at M3.5+, 8,715 at
M4.0+ — identical totals at the lower thresholds. This is a national property of the
catalogue, not a regional quirk. Any "events today" figure must name its catalogue; the
PHIVOLCS local network records M2–3 events routinely.
→ `DataSource.MinimumReliableMagnitude`

**Catalogue completeness changes over time, and this is the trap.** Events per decade rise
from 21 in the 1900s to 5,974 in the 2020s — a 285-fold increase. That is instrumentation,
not seismicity. Over the same 125 years the M6.0+ rate is flat at roughly five per year
(1920s: 6.1, 1970s: 6.1, 1990s: 6.4, 2020s: 6.0). So only magnitude 6 and above can be
compared between eras. A timeline showing rising bars without saying so would invite a
false conclusion, which is why the Time Machine carries an **M6.0+ only** toggle and warns
in place when the scrubber enters the sparse era.

**43% of catalogued depths were never measured.** Four fixed-depth conventions account for
11,790 of 27,241 events: 33 km ×5,581, 10 km ×4,089, 35 km ×1,794, 15 km ×326. 33 km is
the historical NEIC "normal depth" assumption and is the single most common depth in the
archive — it is invisible if you only look at the modern record, and plotting it naively
draws a false flat band through a fifth of the data. `depthError` is populated for these
events, so error-based filtering does not work; detection is the exact-value convention.
→ `DepthQuality.OperatorAssigned`

**The same earthquake has more than one correct magnitude.** 2017 Surigao is Ms 6.7 / 10 km
to PHIVOLCS and Mww 6.5 / 15 km to USGS (`us20008ixa`). Different networks, different
scales. `HazardEvent` therefore holds many `EarthquakeObservation` rows, and no code path
reduces an event to a single magnitude.
→ `HazardEvent` / `EarthquakeObservation`

**Magnitude scales are not interchangeable.** The catalogue is 92.8% `mb`, while nearly
every large event is `mww`. `mb` saturates near M6 and diverges from moment magnitude, so
similarity search that ignored scale would compare the flagship events against a body-wave
population.
→ `MagnitudeType.IsComparableWith`

**Depths reach 667 km.** Slab seismicity beneath the archipelago goes far deeper than the
crustal events most people picture, which is what makes a depth cross-section worth
building.

**PHIVOLCS publishes rendering, not data.** Twelve hazard services are reachable, but bulk
vector query is disabled and vector release requires a signed Data User Agreement. V1
proxies their imagery and stores nothing.
→ [ADR-003](docs/adr/ADR-003-phivolcs-proxy-not-copy.md)

**The Philippine place code has two editions, and they cannot be converted into one another.**
GeoNames publishes the pre-2019 nine-digit PSGC; the PSA publishes a ten-digit register today.
For most provincial municipalities the digits can be re-sliced — Adams is `012801000` and
`0102801000` — but Metro Manila was recoded wholesale, where Quezon City is `137404000` against
`1381300000`. So no code is derived: a computed crosswalk would be right in most of the country
and confidently wrong in the capital. Five places carry no code at all, each for a reason that is
administrative history — Maguindanao was split in 2022 and both halves still share the undivided
province's code, and Isabela City belongs to Basilan but is administered under Region IX.
→ `GeoNamesPlaceSource.UniqueDerivedCodes`

**Fault geometry comes from GEM, not PHIVOLCS, and that is deliberate.** GEM's Philippine
catalogue is openly licensed CC BY-SA 4.0, so it can be stored and queried — 155 traces
including the East Valley Fault, Digdig, Casiguran and 25 subduction thrust segments.
PHIVOLCS publishes far more detailed traces but cannot be copied while the DUA is pending.
GEM is coarser and is not a substitute; both are registered as separate sources so the
difference is visible rather than hidden.
→ [ADR-004](docs/adr/ADR-004-gem-faults-for-v1.md)

## Decisions

- [ADR-001](docs/adr/ADR-001-no-mediatr.md) — hand-written dispatcher instead of MediatR
- [ADR-002](docs/adr/ADR-002-geometry-in-domain.md) — NetTopologySuite permitted in Domain
- [ADR-003](docs/adr/ADR-003-phivolcs-proxy-not-copy.md) — hazard layers proxied, not copied
- [ADR-004](docs/adr/ADR-004-gem-faults-for-v1.md) — GEM faults for V1; PHIVOLCS an upgrade
- [ADR-005](docs/adr/ADR-005-lgu-boundaries-second-spatial-concept.md) — LGU boundaries are a second
  spatial concept, not a replacement for the radius. Accepted; implementation gated on the PSGC
  crosswalk

Current state and remaining work: **[docs/ROADMAP.md](docs/ROADMAP.md)**

## Data sources

Read and stored by the platform:

USGS ComCat · GEM Global Active Faults · DOST-PHIVOLCS · DOST-PAGASA · DOST-MGB ·
NOAA IBTrACS (JTWC, JMA, CMA, HKO, KMA) · GeoNames · OpenStreetMap (town centres, via Overpass)

Fetched by the browser while the map draws, and stored nowhere:

OpenStreetMap via OpenFreeMap · NASA Blue Marble bathymetry · Esri/Maxar imagery ·
AWS Terrain Tiles

OpenStreetMap appears in both lists, and the distinction matters: the basemap is drawn by the
browser and held nowhere, while 1,520 town-centre coordinates *are* stored — under ODbL 1.0, which
is share-alike, so any derived place database inherits it.

Attribution, licence terms and per-source coverage limits are seeded into the database, served by
`GET /api/data-sources`, and rendered by the Sources page — 18 sources, 12 stored and 6 proxied —
so documentation cannot drift from what the system actually reads. The page splits them by
whether the data is held or only displayed, because a proxied source contributes to no figure on
this platform. Client-side basemap and terrain services are credited in their own section,
derived from the same constants the map reads.
