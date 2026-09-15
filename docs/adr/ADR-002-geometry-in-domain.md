# ADR-002 — NetTopologySuite is permitted in the Domain layer

**Status** Accepted · 2026-09-04

## Context

`Calametra.Domain` is required to have no project references and no framework packages.
The `.csproj` enforces the first; the second is a judgement call, and this is the call.

The domain needs geometry. An earthquake has an epicentre. A hazard layer has an extent.
A cyclone has a track. A place has a boundary. These are not storage details — they are
what the entities *are*.

## Decision

`Calametra.Domain` may reference **NetTopologySuite, and nothing else.**

## Reasoning

NetTopologySuite is a pure value-type library: geometry primitives and computational
geometry, with no I/O, no database, no framework, no provider SDK, no configuration. It is
the geospatial equivalent of `System.Numerics` — it happens to ship as a NuGet package
rather than in the BCL.

The alternative is worse. Excluding geometry from the domain means either:

1. **Domain holds `double Latitude, Longitude` only**, and geometry lives in
   Infrastructure. This works for points and fails for hazard polygons and place
   boundaries, where hand-rolling a `MultiPolygon` is absurd. It also pushes "what area
   does this hazard cover" out of the domain, which is a domain question.

2. **Spatial queries move behind ports.** Purer, and rejected on the reference
   architecture's own reasoning (§10 Option B): every query shape needs its own port
   method. Calametra composes radius, time window, magnitude range, depth range, depth
   quality and magnitude scale in one query, so the port would become the six-parameter
   method the architecture explicitly warns against — or would leak `IQueryable` and give
   back the dependency it was meant to remove.

This is the same class of deliberate trade the reference architecture makes for EF Core in
the Application layer: take the dependency, say so plainly, and constrain it with a test.

## Consequences

Good:
- Entities express geospatial concepts directly.
- Query handlers compose real spatial predicates that translate to PostGIS.
- One documented exception rather than a distorted model.

Bad:
- The claim "Domain has zero dependencies" is false, and must not be made. The accurate
  claim is "Domain has one dependency, on a geometry value-type library, and no
  framework or infrastructure dependency of any kind."
- NetTopologySuite version changes touch the innermost layer.

## Verification

Two tests, both in `Calametra.ArchitectureTests.LayerDependencyTests`:

- `Domain_ShouldOnlyReferenceNetTopologySuite` — asserts the referenced assembly list is
  exactly `["NetTopologySuite"]` after excluding BCL assemblies. Any new package fails it.
- `Domain_ShouldNotDependOnAnyFramework` — blocks EF Core, ASP.NET Core,
  `Microsoft.Extensions.*`, Npgsql, Serilog, FluentValidation and `System.Net.Http`.

If the first test fails, the fix is to remove the package or write a new ADR. Widening the
expected list without a recorded reason is how this decision would quietly become "Domain
references whatever is convenient".
