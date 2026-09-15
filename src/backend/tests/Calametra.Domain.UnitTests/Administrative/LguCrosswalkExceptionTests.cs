using Calametra.Domain.Abstractions;
using Calametra.Domain.Administrative;
using Shouldly;

namespace Calametra.Domain.UnitTests.Administrative;

/// <summary>
/// ADR-005 D5: an unpaired unit is valid, not broken — provided somebody wrote down why.
/// </summary>
/// <remarks>
/// The failure these guard against is the tempting one. Thirteen units in the 2Q 2026 register have no
/// nine-digit counterpart, and the cheapest way to make the crosswalk look complete is to pair them with
/// something plausible. An invented pairing is indistinguishable from a real one once stored, so the
/// alternative has to be cheap too: a written reason, attributed, against a citable edition.
/// </remarks>
public sealed class LguCrosswalkExceptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);

    private const string RealReason =
        "The nine-digit edition predates this region: BARMM replaced ARMM in 2019 and the gazetteer's row "
        + "carries no PSGC code at all.";

    [Fact]
    public void A_register_unit_with_no_historical_code_can_be_excused_with_a_reason()
    {
        var unit = Unit("1900000000", "BARMM");

        var accepted = LguCrosswalkException.ForRegisterUnit(
            unit,
            RealReason,
            "A. Reviewer",
            Edition(RegisterProvenance.PsaDirect),
            Now);

        accepted.IsSuccess.ShouldBeTrue();
        accepted.Value.Kind.ShouldBe(CrosswalkExceptionKind.RegisterUnitHasNoHistoricalCode);
        accepted.Value.CanonicalPsgcCode.ShouldBe("1900000000");
        accepted.Value.PlaceId.ShouldBeNull();
        accepted.Value.AcceptedAt.ShouldBe(Now);

        // Denormalised so the record still reads after a later edition recodes or retires the unit, which
        // is the situation these exceptions arise from in the first place.
        accepted.Value.SubjectName.ShouldBe("BARMM");
    }

    [Fact]
    public void A_directory_row_with_no_register_unit_can_be_excused_with_a_reason()
    {
        var accepted = LguCrosswalkException.ForDirectoryRow(
            Guid.CreateVersion7(),
            "133901000",
            "Tondo I / Ii",
            "A district of the City of Manila. The PSGC classifies Manila's districts below the city, so no "
            + "city-or-municipality unit exists to pair with.",
            "A. Reviewer",
            Edition(RegisterProvenance.PsaDirect),
            Now);

        accepted.IsSuccess.ShouldBeTrue();
        accepted.Value.Kind.ShouldBe(CrosswalkExceptionKind.DirectoryRowHasNoRegisterUnit);
        accepted.Value.DirectoryPsgcCode.ShouldBe("133901000");
        accepted.Value.LguId.ShouldBeNull();
    }

    [Fact]
    public void An_exception_without_a_reason_is_refused()
    {
        var result = LguCrosswalkException.ForRegisterUnit(
            Unit("1900000000", "BARMM"),
            "   ",
            "A. Reviewer",
            Edition(RegisterProvenance.PsaDirect),
            Now);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("crosswalk_exception.reason_required");
    }

    [Fact]
    public void A_reason_too_short_to_explain_anything_is_refused()
    {
        // "n/a" and "none" pass a blank check while explaining nothing, and this text is published.
        var result = LguCrosswalkException.ForRegisterUnit(
            Unit("1900000000", "BARMM"),
            "n/a",
            "A. Reviewer",
            Edition(RegisterProvenance.PsaDirect),
            Now);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("crosswalk_exception.reason_too_short");
    }

    [Fact]
    public void An_exception_requires_the_person_accepting_it()
    {
        var result = LguCrosswalkException.ForRegisterUnit(
            Unit("1900000000", "BARMM"),
            RealReason,
            "  ",
            Edition(RegisterProvenance.PsaDirect),
            Now);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("crosswalk_exception.accepted_by_required");
    }

    [Theory]
    [InlineData(RegisterProvenance.Mirror)]
    [InlineData(RegisterProvenance.LocalFile)]
    public void Only_a_PSA_direct_edition_may_certify_that_no_counterpart_exists(
        RegisterProvenance provenance)
    {
        // "This unit has no nine-digit counterpart" is a claim about the register. A mirror's own
        // staleness could be the reason the counterpart appears to be missing, so it may not be cited —
        // the same standard confirmation is held to.
        var result = LguCrosswalkException.ForRegisterUnit(
            Unit("1900000000", "BARMM"),
            RealReason,
            "A. Reviewer",
            Edition(provenance),
            Now);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("crosswalk_exception.edition_not_citable");
    }

    [Fact]
    public void A_directory_exception_requires_a_subject()
    {
        var result = LguCrosswalkException.ForDirectoryRow(
            Guid.Empty,
            "133901000",
            "Tondo",
            RealReason,
            "A. Reviewer",
            Edition(RegisterProvenance.PsaDirect),
            Now);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("crosswalk_exception.subject_required");
    }

    private static Lgu Unit(string canonicalCode, string name) =>
        Lgu.Create(canonicalCode, name, LguLevel.Region, Guid.CreateVersion7(), Now).Value;

    private static PsgcRegisterEdition Edition(RegisterProvenance provenance) =>
        PsgcRegisterEdition.Create(
            "PSGC test edition",
            provenance,
            "C:/psgc/test.xlsx",
            Guid.CreateVersion7(),
            Now,
            Now).Value;
}
