# Backend instructions

These rules extend the repository-root `AGENTS.md` for work under `src/backend`.

## Boundaries and slices

- Preserve dependency direction: Domain has no project references; Application depends on Domain;
  Infrastructure implements Application ports; API and Ingestion compose the system.
- `Calametra.Api/Program.cs` is the only permitted API reference point for Infrastructure types.
- The API and ingestion worker are separate hosts. Do not expose operator/import/review commands as public
  HTTP merely to make every use case look uniform.
- Implement behavior as a cohesive vertical slice: request, internal sealed handler, validator where needed,
  response model, endpoint where public, and focused tests.
- Use the repository's hand-written dispatcher and pipeline. Do not add MediatR without revisiting ADR-001.
- Add every request to the explicit dependency-injection request list, and follow the endpoint/use-case
  naming convention enforced by architecture tests.

## Domain, Application, and errors

- NetTopologySuite is the Domain layer's only permitted non-BCL dependency. Do not widen that exception
  without an ADR and corresponding architecture-test change.
- EF Core composition in Application is intentional. Do not add a generic repository or hide useful spatial
  composition behind broad provider-neutral abstractions.
- Keep the review surface and analytics surface separate. Never expose `LguCodeLink` proposals through
  `IApplicationDbContext`; reader-visible queries may consume only confirmed identity.
- Expected failures use immutable `Result`/`Result<T>` values with stable error codes. HTTP status conversion
  stays centralized at the API boundary. Unexpected failures use the global exception handler.
- Carry cancellation tokens through asynchronous database and provider work.

## PostGIS and persistence

- Keep large geometry and aggregation work in PostgreSQL; do not materialize national geometry into managed
  memory when PostGIS can answer the query.
- Treat spatial column type as meaningful. Avoid casts that change units, boundary semantics, or prevent a
  GiST index from being used.
- For a new or changed spatial query, test exact-edge behavior where relevant and inspect `EXPLAIN ANALYZE`
  on representative simple and fragmented geometry.
- Use versioned rows for boundaries and legislated identity. Supersede rather than mutate or delete history.
- Put views, indexes, constraints, and other schema behavior in EF migrations. Run migrations in integration
  tests against the pinned PostGIS image; never substitute the EF in-memory provider or `EnsureCreated` for
  spatial verification.
- Generated migrations require human review. Preserve source IDs, extract IDs, dates, hashes, geometry
  semantics, and historical rows.

## Current environment and commands

Versions and connection details below are current facts; verify them in `Directory.Build.props`, package
files, `docker-compose.yml`, and appsettings before relying on them. The current stack is .NET 10 with
PostgreSQL/PostGIS, and local Docker maps PostgreSQL to host port 5433.

```powershell
docker compose up -d database

cd src/backend
dotnet ef database update --project src/Calametra.Infrastructure --startup-project src/Calametra.Api
dotnet build Calametra.slnx -c Release
dotnet test Calametra.slnx -c Release
```

Use Testcontainers for API integration tests. If a Debug build is blocked by a running API locking output
DLLs, do not terminate the user's process casually; verify in Release or coordinate before stopping it.
