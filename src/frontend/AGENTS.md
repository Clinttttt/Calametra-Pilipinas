# Frontend instructions

These rules extend the repository-root `AGENTS.md` for work under `src/frontend`.

## Angular structure and state

- Follow the current standalone Angular, strict TypeScript, zoneless, signal-based architecture. Verify exact
  versions in `package.json` rather than treating them as permanent.
- Keep feature code under `features`, shared presentation primitives under `shared/ui`, and durable state or
  API/visual logic under `core` according to existing conventions.
- Prefer small signal-based stores and pure/testable visual functions. Do not introduce a global state
  framework without demonstrated cross-feature complexity.
- Keep `LguSelectionStore` independent from `PlaceStore`. An LGU click selects an administrative polygon;
  it must not trigger representative-point context or a radius query.

## MapLibre interaction

- Preserve deliberate interaction precedence. Earthquake, cyclone, hazard-feature, raster inspection, place,
  and cross-section actions own a click before municipality selection falls back to the land underneath.
- Persistent LGU selection is keyed by canonical PSGC and must survive tile eviction, hover exit, bulk-layer
  disablement, and zooming out. Do not replace persistent selection with tile-local feature state.
- Ordinary boundary rendering follows zoom and visibility rules; selected geometry remains part of the answer
  and is independent of the bulk mesh.
- `applyBasemap` currently preserves application layers and does not call `map.setStyle()`. Do not add
  speculative style reattachment. If a real full-style replacement is introduced, first add regression tests
  for all application sources, layers, and selection state.
- A layer-scoped pointer handler needs a rendered hit target. Do not bind interaction solely to a layer whose
  filter is empty until hover/selection already exists.
- Use the real design tokens. Verify panels over imagery, coastlines, narrow viewports, and map controls;
  avoid inventing token names or claiming screenshots/manual QA that did not occur.

## Reader-facing semantics

- Keep attribution, licence, source mode, coverage limits, missing-unit counts, boundary land-only meaning,
  and area caveats visible through the existing disclosure patterns.
- Do not label offshore events as municipality-contained. Do not turn hazard-specific results into a generic
  total whose meaning changes by row.
- A disabled bulk boundary layer must not remove the selected LGU outline or its inspection answer.

## Verification

Run all three checks before handing off a frontend behavior change:

```powershell
cd src/frontend
npx.cmd tsc --noEmit -p tsconfig.app.json
npm.cmd run build
npm.cmd test
```

Unit tests do not type-check MapLibre expressions. The production build and explicit TypeScript check are
both required. For map changes, add structural interaction tests and manually observe the real map when the
environment permits; report clearly when visual verification was not possible.
