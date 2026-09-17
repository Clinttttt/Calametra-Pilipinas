using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Abstractions.Sources;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Administrative;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Ingestion;

/// <summary>Imports one exact, hashed PSA OpenSTAT official-land-area edition.</summary>
public static class ImportOfficialLandAreas
{
    public sealed record Command : ICommand<Summary>;

    public sealed record Summary(
        Guid EditionId,
        string EditionLabel,
        string MatrixId,
        string PayloadSha256,
        int ActiveLguCount,
        int ImportedLguCount,
        IReadOnlyList<string> MissingCanonicalPsgcCodes,
        IReadOnlyList<string> ExtraSourceCodes,
        bool Activated,
        int EditionsSuperseded);

    internal sealed class Handler(
        IApplicationDbContext context,
        IOfficialLandAreaSource areaSource,
        TimeProvider timeProvider) : ICommandHandler<Command, Summary>
    {
        private static readonly Error SourceNotRegistered = new(
            ErrorType.NotFound,
            "official_land_area.source_not_registered",
            "The PSA official-land-area source is not registered. Seed reference data first.");

        private static readonly Error RegisterNotAvailable = new(
            ErrorType.NotFound,
            "official_land_area.register_not_available",
            "A current PSA register edition is required before official land areas can be matched.");

        private static readonly Error DuplicateSourceCode = new(
            ErrorType.Conflict,
            "official_land_area.duplicate_source_code",
            "The PSA matrix returned the same geographic code more than once.");

        public async Task<Result<Summary>> Handle(Command request, CancellationToken cancellationToken)
        {
            var source = await context.DataSources.SingleOrDefaultAsync(
                candidate => candidate.Slug == areaSource.SourceSlug,
                cancellationToken);

            if (source is null)
            {
                return Result<Summary>.Failure(SourceNotRegistered);
            }

            var registerEdition = await context.PsgcRegisterEditions.SingleOrDefaultAsync(
                edition => edition.SupersededAt == null,
                cancellationToken);

            if (registerEdition is null)
            {
                return Result<Summary>.Failure(RegisterNotAvailable);
            }

            var activeLgus = await context.Lgus
                .Where(lgu => lgu.RegisterEditionId == registerEdition.Id
                    && (lgu.Level == LguLevel.City || lgu.Level == LguLevel.Municipality))
                .OrderBy(lgu => lgu.CanonicalPsgcCode)
                .ToListAsync(cancellationToken);

            var snapshot = await areaSource.ReadAsync(cancellationToken);
            var duplicate = snapshot.Rows
                .GroupBy(row => row.CanonicalPsgcCode, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);

            if (duplicate is not null)
            {
                return Result<Summary>.Failure(DuplicateSourceCode);
            }

            var existing = await context.LguOfficialLandAreaEditions
                .SingleOrDefaultAsync(
                    edition => edition.PayloadSha256 == snapshot.PayloadSha256
                        && edition.RegisterEditionId == registerEdition.Id,
                    cancellationToken);

            if (existing is not null)
            {
                return Result<Summary>.Success(ToSummary(existing, activeLgus.Count, editionsSuperseded: 0));
            }

            var now = timeProvider.GetUtcNow();
            var edition = LguOfficialLandAreaEdition.Create(
                source.Id,
                registerEdition.Id,
                snapshot.Label,
                snapshot.MatrixId,
                snapshot.AccessRoute,
                snapshot.ReferenceYear,
                snapshot.RetrievedAt,
                now).Value;

            edition.RecordAcquisition(
                snapshot.SourceUpdatedAt,
                snapshot.MetadataSha256,
                snapshot.PayloadSha256,
                snapshot.MetadataJson,
                snapshot.PayloadJson,
                snapshot.Attribution);

            var rowsByCode = snapshot.Rows.ToDictionary(
                row => row.CanonicalPsgcCode,
                StringComparer.Ordinal);
            var coverage = OfficialLandAreaCoverage.Measure(
                activeLgus.Select(lgu => lgu.CanonicalPsgcCode),
                snapshot.Rows);
            var missing = coverage.MissingActiveCodes;
            var extras = coverage.ExtraSourceCodes;

            context.LguOfficialLandAreaEditions.Add(edition);

            var imported = 0;

            foreach (var lgu in activeLgus)
            {
                if (!rowsByCode.TryGetValue(lgu.CanonicalPsgcCode, out var row))
                {
                    continue;
                }

                context.LguOfficialLandAreas.Add(LguOfficialLandArea.Record(
                    lgu.Id,
                    edition.Id,
                    lgu.CanonicalPsgcCode,
                    row.AreaSquareKm,
                    OfficialLandAreaBasis.Unspecified,
                    row.SourceLabel,
                    now).Value);

                imported++;
            }

            edition.RecordCoverage(snapshot.Rows.Count, imported, missing, extras);

            var superseded = 0;

            if (edition.Activate(now))
            {
                var previous = await context.LguOfficialLandAreaEditions
                    .Where(candidate => candidate.Id != edition.Id
                        && candidate.SourceId == source.Id
                        && candidate.ActivatedAt != null
                        && candidate.SupersededAt == null)
                    .ToListAsync(cancellationToken);

                foreach (var earlier in previous)
                {
                    earlier.Supersede(edition.Id, now);
                }

                superseded = previous.Count;
            }

            source.RecordRetrieval(now, snapshot.PayloadSha256, snapshot.Label);
            await context.SaveChangesAsync(cancellationToken);

            return Result<Summary>.Success(new Summary(
                edition.Id,
                edition.Label,
                edition.MatrixId,
                edition.PayloadSha256,
                activeLgus.Count,
                imported,
                missing,
                extras,
                edition.IsCurrent,
                superseded));
        }

        private static Summary ToSummary(
            LguOfficialLandAreaEdition edition,
            int activeLguCount,
            int editionsSuperseded) =>
            new(
                edition.Id,
                edition.Label,
                edition.MatrixId,
                edition.PayloadSha256,
                activeLguCount,
                edition.ImportedLguCount,
                Split(edition.MissingCanonicalPsgcCodes),
                Split(edition.ExtraSourceCodes),
                edition.IsCurrent,
                editionsSuperseded);

        private static string[] Split(string? codes) =>
            string.IsNullOrWhiteSpace(codes)
                ? []
                : codes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

internal sealed record OfficialLandAreaCoverage(
    IReadOnlyList<string> MissingActiveCodes,
    IReadOnlyList<string> ExtraSourceCodes)
{
    public static OfficialLandAreaCoverage Measure(
        IEnumerable<string> activeCanonicalCodes,
        IEnumerable<OfficialLandAreaSourceRow> sourceRows)
    {
        var active = activeCanonicalCodes.ToHashSet(StringComparer.Ordinal);
        var source = sourceRows
            .Select(row => row.CanonicalPsgcCode)
            .ToHashSet(StringComparer.Ordinal);

        return new OfficialLandAreaCoverage(
            active.Where(code => !source.Contains(code)).Order(StringComparer.Ordinal).ToArray(),
            source.Where(code => !active.Contains(code)).Order(StringComparer.Ordinal).ToArray());
    }
}
