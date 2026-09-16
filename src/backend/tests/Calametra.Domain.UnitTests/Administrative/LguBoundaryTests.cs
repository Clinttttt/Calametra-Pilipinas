using Calametra.Domain.Administrative;
using Calametra.Domain.Geospatial;
using NetTopologySuite.Geometries;
using Shouldly;

namespace Calametra.Domain.UnitTests.Administrative;

/// <summary>
/// ADR-005 D7: geometry is versioned and never mutated in place.
/// </summary>
public sealed class LguBoundaryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    [Fact]
    public void A_boundary_records_the_extract_it_came_from()
    {
        var extractedAt = new DateTimeOffset(2026, 9, 14, 6, 0, 0, TimeSpan.Zero);

        var boundary = Record(extractedAt: extractedAt, extractVersion: "2026-09-14T05:59:12Z");

        boundary.ExtractedAt.ShouldBe(extractedAt);
        boundary.ExtractVersion.ShouldBe("2026-09-14T05:59:12Z");

        // Valid from the extract, not from when the row happened to be written. A containment figure has
        // to be able to name the boundary edition behind it, and the edition is the extract.
        boundary.ValidFrom.ShouldBe(extractedAt);
        boundary.IsInForce.ShouldBeTrue();
    }

    [Fact]
    public void Superseding_retires_the_version_without_touching_its_geometry()
    {
        var boundary = Record();
        var before = boundary.Geometry;
        var replacement = Guid.CreateVersion7();

        boundary.Supersede(replacement, Now).IsSuccess.ShouldBeTrue();

        boundary.IsInForce.ShouldBeFalse();
        boundary.ValidTo.ShouldBe(Now);
        boundary.SupersededByBoundaryId.ShouldBe(replacement);

        // The whole point of retaining it: a figure published against this shape stays explainable.
        boundary.Geometry.ShouldBeSameAs(before);
    }

    [Fact]
    public void Superseding_twice_keeps_the_version_that_first_replaced_it()
    {
        var boundary = Record();
        var first = Guid.CreateVersion7();

        boundary.Supersede(first, Now);
        boundary.Supersede(Guid.CreateVersion7(), Now.AddDays(1));

        boundary.SupersededByBoundaryId.ShouldBe(first);
        boundary.ValidTo.ShouldBe(Now);
    }

    [Fact]
    public void An_empty_outline_is_refused()
    {
        // An empty geometry would make the unit look covered while containing nothing, and coverage is the
        // figure the interaction phase turns on.
        var result = LguBoundary.Record(
            Guid.CreateVersion7(),
            "0102801000",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            12345L,
            null,
            "0102801000",
            "Adams",
            6,
            Factory.CreateMultiPolygon([]),
            159d,
            Now,
            null,
            false,
            null,
            Now);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("lgu_boundary.geometry_empty");
    }

    [Fact]
    public void An_outline_enclosing_no_area_is_refused()
    {
        var result = LguBoundary.Record(
            Guid.CreateVersion7(),
            "0102801000",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            12345L,
            null,
            "0102801000",
            "Adams",
            6,
            Square(),
            0d,
            Now,
            null,
            false,
            null,
            Now);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("lgu_boundary.area_not_positive");
    }

    [Fact]
    public void A_boundary_must_name_its_source_so_the_licence_travels_with_the_geometry()
    {
        var result = LguBoundary.Record(
            Guid.CreateVersion7(),
            "0102801000",
            Guid.Empty,
            Guid.CreateVersion7(),
            12345L,
            null,
            "0102801000",
            "Adams",
            6,
            Square(),
            159d,
            Now,
            null,
            false,
            null,
            Now);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("lgu_boundary.source_required");
    }

    [Fact]
    public void The_ref_tag_is_kept_verbatim_whatever_edition_it_held()
    {
        // The documented OSM convention says nine digits and the mapped data is largely ten. What a
        // relation actually carried is the evidence for how the polygon was attached to the unit.
        var boundary = Record(refTag: "0102801000");

        boundary.OsmRefTag.ShouldBe("0102801000");
    }

    private static LguBoundary Record(
        DateTimeOffset? extractedAt = null,
        string? extractVersion = null,
        string? refTag = "0102801000") =>
        LguBoundary.Record(
            Guid.CreateVersion7(),
            "0102801000",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            12345L,
            null,
            refTag,
            "Adams",
            6,
            Square(),
            159d,
            extractedAt ?? Now,
            extractVersion,
            false,
            null,
            Now).Value;

    private static MultiPolygon Square()
    {
        var ring = Factory.CreateLinearRing(
        [
            new Coordinate(120.9, 18.4),
            new Coordinate(121.0, 18.4),
            new Coordinate(121.0, 18.5),
            new Coordinate(120.9, 18.5),
            new Coordinate(120.9, 18.4),
        ]);

        return Factory.CreateMultiPolygon([Factory.CreatePolygon(ring)]);
    }
}

/// <summary>
/// Area on the sphere, because a polygon in degrees has no area anyone should quote.
/// </summary>
public sealed class GeodeticAreaTests
{
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    [Fact]
    public void A_degree_square_near_the_equator_is_larger_than_one_far_north()
    {
        // The reason a single scale factor cannot be used: the same one-degree box is about 4% smaller in
        // Batanes than in Tawi-Tawi, and the Philippines spans both.
        var south = GeodeticArea.SquareKilometres(Box(4.0, 121.0, 5.0, 122.0));
        var north = GeodeticArea.SquareKilometres(Box(20.0, 121.0, 21.0, 122.0));

        south.ShouldBeGreaterThan(north);
    }

    [Fact]
    public void A_one_degree_box_at_the_equator_is_about_twelve_thousand_square_kilometres()
    {
        // 111.3 km per degree squared is roughly 12,400 km2. A check on the formula rather than on a
        // dataset: if this drifts, every stated LGU area drifts with it.
        var area = GeodeticArea.SquareKilometres(Box(0.0, 120.0, 1.0, 121.0));

        area.ShouldBeInRange(12_000d, 12_500d);
    }

    [Fact]
    public void A_hole_is_subtracted_so_a_unit_reports_what_it_governs()
    {
        var outer = Factory.CreateLinearRing(
        [
            new Coordinate(120.0, 10.0),
            new Coordinate(121.0, 10.0),
            new Coordinate(121.0, 11.0),
            new Coordinate(120.0, 11.0),
            new Coordinate(120.0, 10.0),
        ]);

        var inner = Factory.CreateLinearRing(
        [
            new Coordinate(120.4, 10.4),
            new Coordinate(120.6, 10.4),
            new Coordinate(120.6, 10.6),
            new Coordinate(120.4, 10.6),
            new Coordinate(120.4, 10.4),
        ]);

        var withHole = GeodeticArea.SquareKilometres(
            Factory.CreateMultiPolygon([Factory.CreatePolygon(outer, [inner])]));

        var solid = GeodeticArea.SquareKilometres(
            Factory.CreateMultiPolygon([Factory.CreatePolygon(outer)]));

        withHole.ShouldBeLessThan(solid);

        // The enclave is a fifth of a degree square, so about 4% of the whole.
        (solid - withHole).ShouldBeInRange(400d, 550d);
    }

    [Fact]
    public void Nothing_has_no_area()
    {
        GeodeticArea.SquareKilometres(null).ShouldBe(0d);
        GeodeticArea.SquareKilometres(Factory.CreateMultiPolygon([])).ShouldBe(0d);
    }

    private static MultiPolygon Box(double south, double west, double north, double east)
    {
        var ring = Factory.CreateLinearRing(
        [
            new Coordinate(west, south),
            new Coordinate(east, south),
            new Coordinate(east, north),
            new Coordinate(west, north),
            new Coordinate(west, south),
        ]);

        return Factory.CreateMultiPolygon([Factory.CreatePolygon(ring)]);
    }
}
