# ADR-004 — GEM active faults are the V1 fault source; PHIVOLCS is an upgrade

**Status** Accepted · 2026-09-04
**Supersedes part of** [ADR-003](ADR-003-phivolcs-proxy-not-copy.md)

## Context

ADR-003 established that PHIVOLCS fault data is proxied, not copied, and named the
cost plainly: no spatial analysis against faults. "Distance to nearest active fault"
was unanswerable, which removes a real feature rather than merely a nice one.

Since then the PHIVOLCS request has been submitted and returned a reference number. The
portal now requires a **signed Data User Agreement** before vector data is released to
academe. That is normal process, not a fee — but it is correspondence, review and an
unknown turnaround, against a **December defence**.

Making a December deliverable depend on another organisation's review queue is not a
schedule, it is a hope.

## Decision

**The GEM Global Active Faults Database becomes the V1 fault source. PHIVOLCS becomes an
upgrade path that can arrive at any time, or not at all.**

Both are registered as distinct `DataSource` rows. Neither replaces the other.

### Verification, 2026-09-04 (CARAGA) and 2026-09-05 (national)

| Check | Result |
|---|---|
| Repository reachable | 200 · `GEMScienceTools/gem-global-active-faults`, updated 2026-08-11 |
| Licence | **CC-BY-SA-4.0** (confirmed via GitHub API, not assumed) |
| Harmonised GeoJSON | 10.6 MB, 13,696 features worldwide |
| `philippines` catalogue | 116 named faults |
| **Imported for the national study area** | **155 traces** (132 named + subduction segments) |
| Attributes | `slip_type`, `net_slip_rate`, `average_dip`, `dip_dir`, `downthrown_side_dir` |
| Slip types present | Sinistral 44, Reverse 32, Subduction_Thrust 25, Dextral 10, Normal 5 |

The imported set includes the fault systems a national platform has to have: the **East
Valley Fault** (Marikina Valley Fault System, the Metro Manila scenario), **Digdig** (the
1990 Luzon M7.8), **Casiguran**, **Lubang**, **Central Mindoro**, **Masbate**,
**Guinayangan**, the **Surigao** and **Offshore Surigao** faults, and 25 subduction thrust
segments covering the Manila, Philippine, Negros and Cotabato Trenches.

The 2017 Surigao earthquake's fault is present, so the flagship demonstration works:
nearest traces to that epicentre are Offshore Surigao at 10.9 km and Surigao Fault at
13.6 km, which matches the accepted attribution.

## Two caveats that must reach the user

**Resolution.** GEM returns 155 traces nationally — principal named faults at regional
scale. The PHIVOLCS service hits its 1,000-record cap on the CARAGA bounding box alone, so
nationally it holds an order of magnitude more detail. GEM is a regional research
compilation, not detailed national mapping. It answers "which major fault system is near
this event" and must not be presented as equivalent to PHIVOLCS. Recorded in
`DataSource.CoverageNotes` and rendered by the About Data page.

A concrete illustration of the gap: Cantilan, Surigao del Sur has **no GEM fault within
50 km** — its nearest is the Surigao Fault at 53.5 km. PHIVOLCS would almost certainly show
closer traces. That is the limitation stated as a number rather than a hedge.

**Share-alike.** CC BY-SA 4.0 is copyleft. Attribution is mandatory and any derived fault
dataset Calametra publishes inherits BY-SA. This is materially different from the PHIVOLCS
terms, and is the second reason the two are separate `DataSource` rows rather than one
merged fault layer — a single layer could not carry two incompatible licences honestly.

## Consequences

Good:
- Fault geometry is in PostGIS, so nearest-fault and faults-within-radius queries work.
- The December deliverable no longer depends on anyone's review queue.
- Provenance is stronger, not weaker: "an openly licensed research compilation, with the
  authoritative national dataset formally requested under an academic DUA" is a better
  position than either alone.

Bad:
- Coarser fault traces in V1 than the PHIVOLCS service can display.
- Two fault layers to reason about, and a UI obligation to explain why both exist.
- BY-SA attaches to derived fault data. Acceptable for an academic project; it would need
  thought if the platform were ever commercialised.

## Enforcement

`IsRedistributable` remains the gate, now with something on each side of it:

- **GEM** — `true`. `HazardLayerDefinition.CreateLocal` and `HazardFeature.Create` succeed;
  geometry is stored.
- **PHIVOLCS** — `false`. Both refuse. The layer stays `RemoteWms`.

Seven tests in `Calametra.Domain.UnitTests.Hazards.HazardRedistributionTests` cover both
directions, including `APendingProxiedLayer_ShouldBePromotable_OncePermissionIsRecorded` —
which is exactly the code path that runs the day the DUA is approved.

## When PHIVOLCS approves

Set `IsRedistributable = true` on the PHIVOLCS source, call `PromoteToLocalVector`, run the
importer. GEM stays. The interface then shows an authoritative national dataset alongside an
open global one, and a user can see for themselves how they differ — which is a better
outcome than having only ever had one.
