# Calametra repository instructions

## Purpose

Calametra Pilipinas is a national Philippine multi-hazard intelligence and historical-exploration
platform. It helps readers inspect evidence, provenance, uncertainty, and spatial relationships.

It is not an official warning service, forecast, emergency-response system, or risk certification.
Never present Calametra as a substitute for PHIVOLCS, PAGASA, NDRRMC, or another competent authority.
Prefer an explicit limitation or honest `Unknown` over a convenient inferred answer.

## Establish context before changing behavior

- Treat the repository—code, migrations, tests, configuration, and tracked ADRs—as the source of truth.
- Inspect the working tree and the relevant slice before editing. Preserve unrelated user changes.
- Before replacing or “simplifying” an established architecture or data concept, inspect its ADR,
  implementation, tests, and recent Git history. Unusual code may encode a deliberate correctness
  decision.
- Current framework versions, database versions, ports, test counts, and dataset counts are environment
  facts, not eternal invariants. Verify them in project/configuration files and the active data edition.
- Do not depend on old agent transcripts for architectural authority.

## Architecture and decision records

The current repository has a .NET backend with Domain, Application, Infrastructure, API, and Ingestion
projects, plus an Angular/MapLibre frontend. The API and ingestion worker are separate hosts sharing the
same Application layer. See the scoped `AGENTS.md` files under `src/backend` and `src/frontend` for
implementation conventions.

Read the relevant ADR before an architectural change:

- [ADR-001](docs/adr/ADR-001-no-mediatr.md): hand-written request dispatcher, not MediatR.
- [ADR-002](docs/adr/ADR-002-geometry-in-domain.md): NetTopologySuite geometry is permitted in Domain.
- [ADR-003](docs/adr/ADR-003-phivolcs-proxy-not-copy.md): PHIVOLCS layers are proxied, not copied.
- [ADR-004](docs/adr/ADR-004-gem-faults-for-v1.md): GEM is the stored V1 fault source; PHIVOLCS remains
  a distinct upgrade path.
- [ADR-005](docs/adr/ADR-005-lgu-boundaries-second-spatial-concept.md): administrative identity,
  boundaries, containment, proximity, delivery, and versioning.

Do not quietly reverse an accepted ADR. A durable reversal requires an explicit superseding decision.

## Administrative identity and crosswalk

- The current PSA ten-digit PSGC is canonical LGU identity. Older nine-digit codes are historical aliases.
- Names are not identifiers. Digit re-slicing and normalized names may propose a match; they may not
  establish one.
- Only reviewed, evidenced `Confirmed` crosswalk rows may feed reader-visible queries. `Proposed` rows are
  a work queue and must remain unreachable from the analytics surface.
- Derive required populations and coverage denominators from the active register edition. Do not hardcode
  a national LGU total as a permanent target.
- Record the edition, reviewer, evidence, and written reason where required. Do not bulk-promote proposals
  merely because they exceed a confidence threshold.
- An unmatched unit, missing alias, or unresolved recoding is valid explicit data—not permission to guess.

## Administrative geometry and spatial meaning

- OCHA COD-AB ADM3, sourced from NAMRIA and PSA, is the canonical concept for current city and municipality
  land outlines. Consult ADR-005 before changing the source or semantics.
- OpenStreetMap municipality relations can include maritime jurisdiction. Retain their provenance and
  history, but never mix them with COD-AB land outlines or use them to fill a COD-AB coverage gap.
- If maritime jurisdiction is introduced, model it as a separate named concept with its own source,
  geometry, licence, and caveat.
- Containment and radius/proximity answer different questions. Containment attributes an event to an
  administrative land polygon; radius/proximity supports equal-distance comparison from a representative
  point. Neither replaces the other.
- Every boundary-derived figure must state the computed area of the geometry actually used and the
  boundary edition/provenance. An official/statistical LGU area is a separate versioned observation;
  neither area may substitute for the other.
  Offshore events remain outside land containment and remain explorable through proximity tools.
- `LguSelectionStore` and `PlaceStore` must remain separate. Selecting an LGU must not silently open or
  mutate the representative-point/radius workflow.
- Bulk boundary visibility and persistent selection are distinct: hiding the boundary mesh must not hide
  the selected unit that forms part of the current answer.

## Hazard-specific spatial semantics

Do not introduce one generic “hazard count” abstraction that erases differences between hazards.

- Earthquakes: count distinct canonical earthquake events, not agency observation rows. Exact-boundary
  epicentres must be included. The current verified implementation uses geography `ST_Intersects`; that is
  an implementation choice supported by tests and benchmarks, not an eternal architectural mandate.
- Tropical cyclones: define the claim before counting. Prefer distinct storms whose storm-centre tracks
  intersect the land polygon. Never count fixes as storms or say “affected” or “landfall” unless those
  conditions are actually measured.
- Faults and trenches: these are mapped features, not events. Report intersecting mapped traces separately
  from event counts and preserve the source-resolution caveat.

For any new spatial predicate, test the edge semantics and inspect `EXPLAIN ANALYZE` against representative
real geometry. Preserve spatial-index use rather than relying on a predicate that is merely logically valid.

## Provenance, licensing, and missing data

- Every source must carry its publisher, attribution, access route, licence/redistribution position,
  vintage or retrieval time, and material coverage limitations.
- Public reachability is not redistribution permission. Do not store or re-serve a source without an
  established right to do so.
- Keep PHIVOLCS proxied unless permission explicitly changes. Keep GEM and PHIVOLCS as distinct sources;
  their authority, resolution, access, and licences differ.
- Keep land geometry and municipal-water geometry distinct. Keep observation values attributed to the
  reporting agency and magnitude scale.
- Missing, ambiguous, incomplete, or repaired data must remain visible in the model and reader-facing
  explanation. Never fabricate geometry, silently backfill identity, hide exceptions, or round coverage up.

## History, migrations, and destructive changes

- Boundaries and legislated administrative identities are versioned. Add and supersede records; do not
  overwrite history in place.
- Preserve upstream identifiers, source observations, dated extracts, hashes, and review evidence needed
  to audit a result later.
- Put schema-dependent views, materialized views, indexes, constraints, and functions in migrations. A
  hand-run local script is not a complete schema.
- Integration tests must exercise migrations against real PostGIS; `EnsureCreated` is not a substitute.
- Review generated migrations for destructive drops, mistaken renames, fabricated defaults, and loss of
  provenance. Do not delete or rewrite historical records merely to make a join or count tidy.

## Verification and commits

Run verification proportionate to the changed scope. For changes crossing API contracts or spatial
semantics, run both relevant suites and real PostGIS integration tests.

```powershell
# Backend
cd src/backend
dotnet build Calametra.slnx -c Release
dotnet test Calametra.slnx -c Release

# Frontend
cd src/frontend
npx.cmd tsc --noEmit -p tsconfig.app.json
npm.cmd run build
npm.cmd test
```

The frontend test runner does not replace TypeScript type-checking. A successful unit suite alone is not a
successful frontend verification.

Before committing: run `git diff --check`, review the complete diff and staged file list, report skipped
verification, and keep unrelated or user-owned files out of the commit. Do not claim visual/manual behavior
was verified unless it was actually observed. Commit or push only when the user requests it.
