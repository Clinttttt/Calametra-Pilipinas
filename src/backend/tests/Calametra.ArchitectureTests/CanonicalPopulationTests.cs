using System.Text.RegularExpressions;

using Shouldly;

namespace Calametra.ArchitectureTests;

/// <summary>
/// Gate 2's review population must be derived from the active canonical PSGC edition, never written down.
/// </summary>
/// <remarks>
/// <para>
/// The register changes every quarter. The mirror this platform first loaded reported 17 regions and
/// 1,634 cities and municipalities; PSA 2Q 2026 reports 18 regions and 1,642. A figure recorded in the
/// source or in the ADR would have been correct for one quarter and then quietly wrong — and because it
/// looks like a specification, it would go on being checked against instead of questioned.
/// </para>
/// <para>
/// This is asserted over the text of the readiness slice rather than its behaviour, because the failure
/// being prevented is a plausible edit and not a broken calculation: a developer wanting a friendlier
/// message writes "0 of 1,642 confirmed", and every behavioural test still passes on the day it is
/// written. Only the calendar makes it wrong.
/// </para>
/// </remarks>
public sealed class CanonicalPopulationTests
{
    /// <summary>
    /// Counts that plausibly assert a national LGU population. Deliberately not every four-digit number:
    /// a year, an event id or a byte length is not a claim about how many municipalities exist.
    /// </summary>
    private static readonly string[] ForbiddenPopulationLiterals =
    [
        "1642", "1,642",
        "1647", "1,647",
        "1646", "1,646",
        "1634", "1,634",
        "1732", "1,732",
        "1493", "1,493",
        "1488", "1,488",
    ];

    [Fact]
    public void The_readiness_slice_records_no_canonical_population_figure()
    {
        var source = ReadSlice("GetLguCrosswalkReadiness.cs");

        foreach (var literal in ForbiddenPopulationLiterals)
        {
            source.ShouldNotContain(
                literal,
                Case.Sensitive,
                $"The readiness report must derive its population from the active edition. '{literal}' "
                + "is a property of one quarter's publication, and a register edition is the only thing "
                + "entitled to state how many units exist.");
        }
    }

    [Fact]
    public void The_import_slice_records_no_canonical_population_figure()
    {
        var source = ReadSlice("ImportPsgcRegister.cs");

        foreach (var literal in ForbiddenPopulationLiterals)
        {
            source.ShouldNotContain(
                literal,
                Case.Sensitive,
                $"The import must report what it read, not compare it against '{literal}'. A publication "
                + "that disagrees with an expected count is news, not an error.");
        }
    }

    [Fact]
    public void Gate_two_reads_its_population_from_the_edition()
    {
        // The positive half of the assertion. Forbidding literals would otherwise be satisfied by a slice
        // that had stopped reporting a population at all.
        var source = ReadSlice("GetLguCrosswalkReadiness.cs");

        source.ShouldContain("edition.CityCount + edition.MunicipalityCount");
        source.ShouldContain("edition.ProvinceCount");
        source.ShouldContain("edition.RegionCount");
    }

    [Fact]
    public void The_decision_record_states_no_target_population()
    {
        // ADR-005 condition 2 originally read "for all 1,647 cities and municipalities, 86 provinces and
        // 17 regions" — a figure that was already three editions stale by the time the gate was built.
        var adr = ReadDecisionRecord();

        // Numerals are permitted where the ADR reports what a specific edition held, which is history.
        // What is forbidden is stating a population the crosswalk must reach.
        var target = new Regex(
            @"(?:for all|must (?:cover|reach)|requires)\s+[\d,]{3,6}\s+(?:cities|units|municipalities)",
            RegexOptions.IgnoreCase);

        target.IsMatch(adr).ShouldBeFalse(
            "ADR-005 must not name a target population. The active canonical edition states it.");
    }

    private static string ReadSlice(string fileName) =>
        File.ReadAllText(Path.Combine(
            SolutionRoot(),
            "src",
            "Calametra.Application",
            "Features",
            "Administrative",
            fileName));

    private static string ReadDecisionRecord() =>
        File.ReadAllText(Path.Combine(
            SolutionRoot(),
            "..",
            "..",
            "docs",
            "adr",
            "ADR-005-lgu-boundaries-second-spatial-concept.md"));

    /// <summary>
    /// Walks up from the test binary to the backend solution directory.
    /// </summary>
    private static string SolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "Calametra.slnx")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("Could not locate Calametra.slnx above the test binary.");

        return directory.FullName;
    }
}
