using Calametra.Domain.Administrative;
using Shouldly;

namespace Calametra.Domain.UnitTests.Administrative;

/// <summary>
/// The invariants that make ADR-005 D4 real rather than aspirational.
/// </summary>
/// <remarks>
/// Every test here corresponds to a sentence in the decision record. The rule that matters most is that
/// a matcher cannot produce a readable row: if <c>Confirm</c> could be reached without a reviewer, an
/// evidence value and a citable edition, then the review gate would be documentation rather than
/// enforcement, and every figure derived from the crosswalk would rest on an algorithm's guess.
/// </remarks>
public sealed class LguCodeLinkTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static PsgcRegisterEdition Edition(RegisterProvenance provenance) =>
        PsgcRegisterEdition.Create(
            "PSGC test edition",
            provenance,
            "https://example.test/psgc",
            Guid.CreateVersion7(),
            Now,
            Now).Value;

    private static LguCodeLink Proposal(
        LguLinkEvidence evidence = LguLinkEvidence.RegisterMatch,
        bool namesAgree = true) =>
        LguCodeLink.Propose(
            Guid.CreateVersion7(),
            "012801000",
            Guid.CreateVersion7(),
            evidence,
            "test basis",
            namesAgree,
            "test-matcher/1.0",
            Guid.CreateVersion7(),
            Now).Value;

    [Fact]
    public void Propose_produces_a_row_that_is_not_readable()
    {
        var link = Proposal();

        link.Status.ShouldBe(LguLinkStatus.Proposed);
        link.IsReadable.ShouldBeFalse();
        link.ReviewedBy.ShouldBeNull();
        link.ConfirmedAgainstEditionId.ShouldBeNull();
    }

    [Fact]
    public void Propose_requires_an_attributable_matcher()
    {
        var result = LguCodeLink.Propose(
            Guid.CreateVersion7(),
            "012801000",
            null,
            LguLinkEvidence.DigitReslice,
            "basis",
            namesAgree: true,
            proposedBy: "   ",
            Guid.CreateVersion7(),
            Now);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("lgu_link.proposed_by_required");
    }

    [Theory]
    [InlineData("12801000")]      // eight digits
    [InlineData("0102801000")]    // the ten-digit canonical form, which is not a historical code
    [InlineData("01280100A")]     // nine characters, not all digits
    public void Propose_rejects_anything_that_is_not_a_nine_digit_code(string code)
    {
        var result = LguCodeLink.Propose(
            Guid.CreateVersion7(),
            code,
            null,
            LguLinkEvidence.RegisterMatch,
            "basis",
            namesAgree: true,
            "test-matcher/1.0",
            Guid.CreateVersion7(),
            Now);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("lgu_link.historical_code_length");
    }

    [Fact]
    public void Confirm_makes_the_row_readable_and_records_who_is_accountable()
    {
        var link = Proposal();

        var result = link.Confirm(
            LguLinkEvidence.RegisterMatch,
            "A. Reviewer",
            reason: null,
            Edition(RegisterProvenance.PsaDirect),
            Now);

        result.IsSuccess.ShouldBeTrue();
        link.Status.ShouldBe(LguLinkStatus.Confirmed);
        link.IsReadable.ShouldBeTrue();
        link.ReviewedBy.ShouldBe("A. Reviewer");
        link.ReviewedAt.ShouldBe(Now);
        link.ConfirmedAgainstEditionId.ShouldNotBeNull();
    }

    [Fact]
    public void Confirm_refuses_an_edition_that_did_not_come_from_the_PSA()
    {
        // The gate that decides ADR-005 condition 1. A mirror may be loaded, matched against and
        // reported on; it cannot certify a pairing, however faithful its copy looks.
        var link = Proposal();

        var result = link.Confirm(
            LguLinkEvidence.RegisterMatch,
            "A. Reviewer",
            reason: null,
            Edition(RegisterProvenance.Mirror),
            Now);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("lgu_link.edition_not_citable");
        link.Status.ShouldBe(LguLinkStatus.Proposed);
    }

    [Fact]
    public void Confirm_refuses_a_digit_reslice_whose_names_disagree()
    {
        // The rule that fails in the capital: Quezon City is 137404000 against 1381300000 and does not
        // re-slice at all. Allowing this would let the algorithm establish the crosswalk with a person's
        // name on it.
        var link = Proposal(LguLinkEvidence.DigitReslice, namesAgree: false);

        var result = link.Confirm(
            LguLinkEvidence.DigitReslice,
            "A. Reviewer",
            reason: null,
            Edition(RegisterProvenance.PsaDirect),
            Now);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("lgu_link.reslice_needs_name_agreement");
    }

    [Fact]
    public void Confirm_allows_a_name_disagreement_to_be_settled_by_manual_review_with_a_reason()
    {
        var link = Proposal(LguLinkEvidence.DigitReslice, namesAgree: false);

        var result = link.Confirm(
            LguLinkEvidence.ManualReview,
            "A. Reviewer",
            "PSA 2Q 2026 register page 14 pairs these units; the directory abbreviates the city name.",
            Edition(RegisterProvenance.PsaDirect),
            Now);

        result.IsSuccess.ShouldBeTrue();
        link.Evidence.ShouldBe(LguLinkEvidence.ManualReview);
        link.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Confirm_on_manual_review_requires_a_written_reason()
    {
        var link = Proposal();

        var result = link.Confirm(
            LguLinkEvidence.ManualReview,
            "A. Reviewer",
            reason: "  ",
            Edition(RegisterProvenance.PsaDirect),
            Now);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("lgu_link.reason_required");
    }

    [Fact]
    public void Confirm_requires_a_reviewer()
    {
        var link = Proposal();

        var result = link.Confirm(
            LguLinkEvidence.RegisterMatch,
            reviewedBy: "",
            reason: null,
            Edition(RegisterProvenance.PsaDirect),
            Now);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("lgu_link.reviewer_required");
    }

    [Fact]
    public void A_decided_row_is_not_overwritten_by_a_second_decision()
    {
        var link = Proposal();

        link.Confirm(
            LguLinkEvidence.RegisterMatch,
            "A. Reviewer",
            null,
            Edition(RegisterProvenance.PsaDirect),
            Now).IsSuccess.ShouldBeTrue();

        var second = link.Reject("B. Reviewer", "changed my mind", Now);

        second.IsFailure.ShouldBeTrue();
        second.Error!.Code.ShouldBe("lgu_link.not_proposed");
        link.Status.ShouldBe(LguLinkStatus.Confirmed);
    }

    [Fact]
    public void Reject_requires_a_reason_so_the_rejection_rate_can_be_explained()
    {
        var link = Proposal();

        var result = link.Reject("A. Reviewer", "   ", Now);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("lgu_link.rejection_reason_required");
    }

    [Fact]
    public void Reject_retains_the_row_so_it_can_be_counted()
    {
        var link = Proposal();

        link.Reject("A. Reviewer", "The register pairs this code with a different unit.", Now)
            .IsSuccess.ShouldBeTrue();

        link.Status.ShouldBe(LguLinkStatus.Rejected);
        link.IsReadable.ShouldBeFalse();
        link.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Supersede_retires_an_unreviewed_proposal_without_deleting_it()
    {
        var link = Proposal();

        link.Supersede(Now).IsSuccess.ShouldBeTrue();

        link.Status.ShouldBe(LguLinkStatus.Superseded);
        link.IsReadable.ShouldBeFalse();
    }

    [Fact]
    public void Supersede_leaves_a_confirmed_pairing_alone()
    {
        // A review is a person's decision against a stated publication. A newer register arriving may make
        // it worth revisiting, which is a judgement for a reviewer — an import does not retract it.
        var link = Proposal();

        link.Confirm(
            LguLinkEvidence.RegisterMatch,
            "A. Reviewer",
            null,
            Edition(RegisterProvenance.PsaDirect),
            Now).IsSuccess.ShouldBeTrue();

        var result = link.Supersede(Now.AddDays(1));

        result.IsFailure.ShouldBeTrue();
        link.Status.ShouldBe(LguLinkStatus.Confirmed);
    }

    [Fact]
    public void A_proposal_must_name_the_edition_it_was_made_against()
    {
        var result = LguCodeLink.Propose(
            Guid.CreateVersion7(),
            "012801000",
            null,
            LguLinkEvidence.RegisterMatch,
            "basis",
            namesAgree: true,
            "test-matcher/1.0",
            proposedAgainstEditionId: Guid.Empty,
            Now);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("lgu_link.edition_required");
    }
}

/// <summary>
/// Identity rules for the canonical unit.
/// </summary>
public sealed class LguTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("012801000")]     // the nine-digit edition, which is an alias and not an identity
    [InlineData("01028010001")]   // eleven digits
    [InlineData("010280100A")]    // ten characters, not all digits
    public void Create_refuses_a_canonical_code_that_is_not_ten_digits(string code)
    {
        var result = Lgu.Create(code, "Adams", LguLevel.Municipality, Guid.CreateVersion7(), Now);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("lgu.canonical_code_length");
    }

    [Fact]
    public void Create_accepts_the_ten_digit_register_code()
    {
        var result = Lgu.Create("0102801000", "Adams", LguLevel.Municipality, Guid.CreateVersion7(), Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CanonicalPsgcCode.ShouldBe("0102801000");
        result.Value.IsCity.ShouldBeFalse();
    }

    [Fact]
    public void A_register_stated_historical_code_is_kept_as_evidence_not_as_identity()
    {
        var lgu = Lgu.Create("0102801000", "Adams", LguLevel.Municipality, Guid.CreateVersion7(), Now)
            .Value
            .WithRegisterStatedHistoricalCode("012801000");

        lgu.RegisterStatedHistoricalCode.ShouldBe("012801000");
        lgu.CanonicalPsgcCode.ShouldBe("0102801000");
    }
}

/// <summary>
/// Whether an edition may certify anything, which is ADR-005's first gate condition.
/// </summary>
public sealed class PsgcRegisterEditionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(RegisterProvenance.PsaDirect, true)]
    [InlineData(RegisterProvenance.Mirror, false)]
    [InlineData(RegisterProvenance.LocalFile, false)]
    public void Only_a_PSA_publication_may_certify_a_crosswalk(
        RegisterProvenance provenance,
        bool expected)
    {
        var edition = PsgcRegisterEdition.Create(
            "PSGC test edition",
            provenance,
            "https://example.test/psgc",
            Guid.CreateVersion7(),
            Now,
            Now).Value;

        edition.IsCitableAsAuthority.ShouldBe(expected);
    }

    [Fact]
    public void Observed_composition_is_recorded_so_staleness_is_measured_rather_than_trusted()
    {
        // 17 regions is the signal that established the open mirror predates the 2024 creation of the
        // Negros Island Region, whatever its label claims.
        var edition = PsgcRegisterEdition.Create(
                "PSGC mirror snapshot",
                RegisterProvenance.Mirror,
                "https://example.test/psgc",
                Guid.CreateVersion7(),
                Now,
                Now).Value
            .WithObservedComposition(17, 81, 148, 1486, new DateTimeOffset(2022, 8, 27, 0, 0, 0, TimeSpan.Zero), "mirror");

        edition.RegionCount.ShouldBe(17);
        edition.ProvinceCount.ShouldBe(81);
        edition.UpstreamLastModified!.Value.Year.ShouldBe(2022);
    }

    [Fact]
    public void The_acquisition_chain_is_recorded_and_the_hash_is_normalised()
    {
        var edition = Edition(RegisterProvenance.PsaDirect)
            .WithAcquisition(
                new DateOnly(2026, 6, 30),
                "PSGC-2Q-2026-Publication-Datafile.xlsx",
                "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789",
                "Downloaded from psa.gov.ph in a browser session on 2026-09-15.");

        edition.PublicationDate.ShouldBe(new DateOnly(2026, 6, 30));
        edition.OriginalFileName.ShouldBe("PSGC-2Q-2026-Publication-Datafile.xlsx");

        // Lowercased on the way in, so two records of the same file cannot differ by case alone.
        edition.FileSha256.ShouldBe("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789");
        edition.AcquisitionNote.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void An_edition_is_current_until_it_is_superseded()
    {
        var mirror = Edition(RegisterProvenance.Mirror);
        var replacement = Guid.CreateVersion7();

        mirror.IsCurrent.ShouldBeTrue();

        mirror.Supersede(replacement, Now);

        mirror.IsCurrent.ShouldBeFalse();
        mirror.SupersededByEditionId.ShouldBe(replacement);
        mirror.SupersededAt.ShouldBe(Now);
    }

    [Fact]
    public void Superseding_twice_keeps_the_edition_that_first_replaced_it()
    {
        // Order of arrival is the fact worth keeping. Rewriting the pointer on a second import would lose
        // which edition actually retired this one.
        var mirror = Edition(RegisterProvenance.Mirror);
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        mirror.Supersede(first, Now);
        mirror.Supersede(second, Now.AddDays(1));

        mirror.SupersededByEditionId.ShouldBe(first);
        mirror.SupersededAt.ShouldBe(Now);
    }

    private static PsgcRegisterEdition Edition(RegisterProvenance provenance) =>
        PsgcRegisterEdition.Create(
            "PSGC test edition",
            provenance,
            "C:/psgc/test.xlsx",
            Guid.CreateVersion7(),
            Now,
            Now).Value;
}
