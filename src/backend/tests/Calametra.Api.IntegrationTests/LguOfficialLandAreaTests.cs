using System.Net;
using System.Net.Http.Json;
using Calametra.Application.Features.Administrative;
using Calametra.Domain.Administrative;
using Calametra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Calametra.Api.IntegrationTests;

[Collection(PostgisCollection.Name)]
public sealed class LguOfficialLandAreaTests(PostgisApiFixture fixture)
{
    private const string LanuzaCode = "1606810000";
    private const string MissingAreaCode = "9900000011";
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    [Fact]
    public async Task Lanuza_keeps_published_and_boundary_geometry_areas_distinct()
    {
        await ArrangeAsync();

        using var client = fixture.CreateClient();
        var response = await client.GetAsync($"/api/lgus/{LanuzaCode}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetAdministrativeUnit.Response>();

        body.ShouldNotBeNull();
        body.CanonicalPsgcCode.ShouldBe(LanuzaCode);
        body.OfficialLandArea.ShouldNotBeNull();
        body.OfficialLandArea.SquareKm.ShouldBe(292.27m);
        body.OfficialLandArea.Basis.ShouldBe(nameof(OfficialLandAreaBasis.Unspecified));
        body.OfficialLandArea.MatrixId.ShouldBe("1A6DLPD0");

        await fixture.WithContextAsync(async context =>
        {
            var boundaryArea = await context.LguBoundaries
                .Where(boundary => boundary.CanonicalPsgcCode == LanuzaCode && boundary.ValidTo == null)
                .Select(boundary => boundary.AreaSquareKm)
                .SingleAsync();

            boundaryArea.ShouldBe(318.6d);
            ((decimal)boundaryArea).ShouldNotBe(body.OfficialLandArea.SquareKm);
        });
    }

    [Fact]
    public async Task Selects_only_the_current_official_area_edition()
    {
        await ArrangeAsync();

        using var client = fixture.CreateClient();
        var body = await client.GetFromJsonAsync<GetAdministrativeUnit.Response>(
            $"/api/lgus/{LanuzaCode}");

        body.ShouldNotBeNull();
        body.OfficialLandArea.ShouldNotBeNull();
        body.OfficialLandArea.SquareKm.ShouldBe(292.27m);
        body.OfficialLandArea.ReferenceYear.ShouldBe(2019);

        await fixture.WithContextAsync(async context =>
        {
            var editions = await context.LguOfficialLandAreaEditions
                .OrderBy(edition => edition.ReferenceYear)
                .ToListAsync();

            editions.Count.ShouldBe(2);
            editions.Single(edition => edition.ReferenceYear == 2013).SupersededAt.ShouldNotBeNull();
            editions.Single(edition => edition.ReferenceYear == 2019).IsCurrent.ShouldBeTrue();

            // Boundary history is independent: activating the newer statistical edition did not retire it.
            (await context.LguBoundaries.SingleAsync(
                boundary => boundary.CanonicalPsgcCode == LanuzaCode)).ValidTo.ShouldBeNull();
        });
    }

    [Fact]
    public async Task Missing_official_area_never_falls_back_to_boundary_geometry_area()
    {
        await ArrangeAsync();

        using var client = fixture.CreateClient();
        var body = await client.GetFromJsonAsync<GetAdministrativeUnit.Response>(
            $"/api/lgus/{MissingAreaCode}");

        body.ShouldNotBeNull();
        body.OfficialLandArea.ShouldBeNull();

        await fixture.WithContextAsync(async context =>
            (await context.LguBoundaries.SingleAsync(
                boundary => boundary.CanonicalPsgcCode == MissingAreaCode))
                .AreaSquareKm.ShouldBe(111.11d));
    }

    private async Task ArrangeAsync() =>
        await fixture.WithContextAsync(async context =>
        {
            if (await context.Lgus.AnyAsync(lgu => lgu.CanonicalPsgcCode == LanuzaCode))
            {
                return;
            }

            var identitySource = await TestData.EnsureSourceAsync(context, "official-area-test-identity");
            var areaSource = await TestData.EnsureSourceAsync(context, "official-area-test-psa");
            var boundarySource = await TestData.EnsureSourceAsync(context, "official-area-test-cod-ab");

            identitySource.WithCoverage(null, "Synthetic PSGC identity source for official-area tests.");
            areaSource.WithCoverage(null, "Synthetic PSA official-area source for integration tests.");
            boundarySource.WithCoverage(null, "Synthetic COD-AB source for area-separation tests.");

            var register = PsgcRegisterEdition.Create(
                "PSGC 2Q 2026",
                RegisterProvenance.PsaDirect,
                "test",
                identitySource.Id,
                Now,
                Now).Value;
            context.PsgcRegisterEditions.Add(register);

            var lanuza = Lgu.Create(
                LanuzaCode,
                "Lanuza",
                LguLevel.Municipality,
                register.Id,
                Now).Value;
            var missing = Lgu.Create(
                MissingAreaCode,
                "Boundary Only Municipality",
                LguLevel.Municipality,
                register.Id,
                Now).Value;
            context.Lgus.AddRange(lanuza, missing);

            var extract = LguBoundaryExtract.Create(
                boundarySource.Id,
                "OCHA COD-AB Philippines ADM3",
                BoundaryProvenance.OchaCodAb,
                "test",
                Now).Value;
            context.LguBoundaryExtracts.Add(extract);

            var oldEdition = Edition(areaSource.Id, register.Id, 2013, 'a');
            oldEdition.RecordCoverage(1, 1, [], []);
            oldEdition.Activate(Now).ShouldBeTrue();

            var currentEdition = Edition(areaSource.Id, register.Id, 2019, 'b');
            currentEdition.RecordCoverage(1, 1, [], []);
            currentEdition.Activate(Now.AddDays(1)).ShouldBeTrue();
            oldEdition.Supersede(currentEdition.Id, Now.AddDays(1));

            context.LguOfficialLandAreaEditions.AddRange(oldEdition, currentEdition);
            context.LguOfficialLandAreas.AddRange(
                LguOfficialLandArea.Record(
                    lanuza.Id,
                    oldEdition.Id,
                    LanuzaCode,
                    290.60m,
                    OfficialLandAreaBasis.CadastralSurvey,
                    "Lanuza",
                    Now).Value,
                LguOfficialLandArea.Record(
                    lanuza.Id,
                    currentEdition.Id,
                    LanuzaCode,
                    292.27m,
                    OfficialLandAreaBasis.Unspecified,
                    "Lanuza",
                    Now).Value);

            context.LguBoundaries.AddRange(
                Boundary(lanuza.Id, LanuzaCode, boundarySource.Id, extract.Id, 318.6d),
                Boundary(missing.Id, MissingAreaCode, boundarySource.Id, extract.Id, 111.11d));

            await context.SaveChangesAsync();
        });

    private static LguOfficialLandAreaEdition Edition(
        Guid sourceId,
        Guid registerEditionId,
        int referenceYear,
        char hashCharacter)
    {
        var edition = LguOfficialLandAreaEdition.Create(
            sourceId,
            registerEditionId,
            $"PSA official land area {referenceYear}",
            "1A6DLPD0",
            "test",
            referenceYear,
            Now,
            Now).Value;

        edition.RecordAcquisition(
            Now,
            new string(hashCharacter, 64),
            new string(hashCharacter, 64),
            "{}",
            "{}",
            "Philippine Statistics Authority; Land Management Bureau");

        return edition;
    }

    private static LguBoundary Boundary(
        Guid lguId,
        string code,
        Guid sourceId,
        Guid extractId,
        double area) =>
        LguBoundary.Record(
            lguId,
            code,
            sourceId,
            extractId,
            null,
            code,
            code,
            "test",
            0,
            Square(),
            area,
            Now,
            "test",
            false,
            null,
            Now).Value;

    private static MultiPolygon Square()
    {
        var ring = Factory.CreateLinearRing(
        [
            new Coordinate(126.00, 9.00),
            new Coordinate(126.10, 9.00),
            new Coordinate(126.10, 9.10),
            new Coordinate(126.00, 9.10),
            new Coordinate(126.00, 9.00),
        ]);

        return Factory.CreateMultiPolygon([Factory.CreatePolygon(ring)]);
    }
}
