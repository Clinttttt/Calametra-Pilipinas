using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Administrative;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Administrative;

/// <summary>Administrative identity and independently sourced current official land area.</summary>
public static class GetAdministrativeUnit
{
    public sealed record Query(string CanonicalPsgcCode) : IQuery<Response>;

    public sealed record OfficialLandArea(
        decimal SquareKm,
        string Basis,
        string EditionLabel,
        int ReferenceYear,
        string MatrixId,
        DateTimeOffset? SourceUpdatedAt,
        string ProvenanceLabel,
        string Attribution);

    public sealed record Response(
        string CanonicalPsgcCode,
        string Name,
        string Level,
        string RegisterEdition,
        OfficialLandArea? OfficialLandArea);

    internal sealed class Validator : AbstractValidator<Query>
    {
        public Validator() =>
            RuleFor(query => query.CanonicalPsgcCode)
                .NotEmpty()
                .Matches("^[0-9]{10}$")
                .WithMessage("The canonical PSGC code must contain exactly ten digits.");
    }

    internal sealed class Handler(IApplicationDbContext context) : IQueryHandler<Query, Response>
    {
        public async Task<Result<Response>> Handle(Query request, CancellationToken cancellationToken)
        {
            var unit = await (
                    from lgu in context.Lgus.AsNoTracking()
                    join register in context.PsgcRegisterEditions.AsNoTracking()
                        on lgu.RegisterEditionId equals register.Id
                    where lgu.CanonicalPsgcCode == request.CanonicalPsgcCode
                    select new
                    {
                        lgu.Id,
                        lgu.CanonicalPsgcCode,
                        lgu.Name,
                        lgu.Level,
                        RegisterEdition = register.Label,
                    })
                .SingleOrDefaultAsync(cancellationToken);

            if (unit is null)
            {
                return Result<Response>.Failure(LguErrors.NotFound);
            }

            var published = await (
                    from area in context.LguOfficialLandAreas.AsNoTracking()
                    join edition in context.LguOfficialLandAreaEditions.AsNoTracking()
                        on area.EditionId equals edition.Id
                    where area.LguId == unit.Id
                        && edition.ActivatedAt != null
                        && edition.SupersededAt == null
                    select new
                    {
                        area.AreaSquareKm,
                        area.Basis,
                        edition.Label,
                        edition.ReferenceYear,
                        edition.MatrixId,
                        edition.SourceUpdatedAt,
                        edition.Attribution,
                    })
                .SingleOrDefaultAsync(cancellationToken);

            var official = published is null
                ? null
                : new OfficialLandArea(
                    published.AreaSquareKm,
                    published.Basis.ToString(),
                    published.Label,
                    published.ReferenceYear,
                    published.MatrixId,
                    published.SourceUpdatedAt,
                    IsSpecialGeographicArea(unit.CanonicalPsgcCode)
                        ? "PSA 2024 POPCEN / MENRE-BARMM"
                        : $"PSA 2024 POPCEN / DENR-LMB {published.ReferenceYear}",
                    published.Attribution);

            return Result<Response>.Success(new Response(
                unit.CanonicalPsgcCode,
                unit.Name,
                unit.Level.ToString(),
                unit.RegisterEdition,
                official));
        }

        // The matrix metadata explicitly identifies the Special Geographic Area as the MENRE-BARMM
        // exception to the LMB masterlist. The 19999 PSGC family is that published administrative group;
        // no name matching or broader source attribution is inferred.
        private static bool IsSpecialGeographicArea(string canonicalPsgcCode) =>
            canonicalPsgcCode.StartsWith("19999", StringComparison.Ordinal);
    }
}
