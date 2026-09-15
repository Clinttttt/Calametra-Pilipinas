using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.Administrative;

namespace Calametra.Ingestion;

/// <summary>
/// Accepts the D5 exception population, each with a written reason drawn from administrative history.
/// </summary>
/// <remarks>
/// <para>
/// <b>These reasons are the deliverable, not the counts.</b> ADR-005 D5 requires each exception to carry a
/// reason that is rendered to the reader, so what is written here is published text and is held to the
/// standard of published text: every claim is checkable, and where a date or an instrument is named it is
/// because it was verified rather than recalled.
/// </para>
/// <para>
/// Held in the ingestion host rather than in the application layer because these are findings about this
/// dataset at this moment, not rules. A later edition may pair some of them — the PSA may publish a
/// correspondence for the Maguindanao halves — and when it does, the finding changes and the code that
/// enforces the crosswalk should not have to.
/// </para>
/// <para>
/// Two facts do the work in almost every reason below. The gazetteer holds five rows with no PSGC code at
/// all: BARMM, Basilan, Cotabato City, Maguindanao del Norte and Maguindanao del Sur. And the register's
/// stated correspondence for three of those points at nine-digit codes the gazetteer never published —
/// <c>150000000</c>, <c>150700000</c>, <c>129804000</c> — so even the register's own pairing has nothing
/// on the other side to attach to. Both were measured against this database, not assumed.
/// </para>
/// </remarks>
internal static class LguExceptionRunner
{
    /// <summary>
    /// Register units for which no nine-digit code exists. Keyed by the ten-digit canonical code.
    /// </summary>
    private static readonly (string CanonicalCode, string Reason)[] UnitExceptions =
    [
        ("1800000000",
            "The Negros Island Region was created in 2024, long after the nine-digit PSGC was published, "
            + "so no nine-digit code exists for it. Its creation moved Negros Occidental out of Region VI "
            + "and Negros Oriental and Siquijor out of Region VII, which recoded every city and "
            + "municipality in those three provinces. The matcher's one candidate for this region was "
            + "100000000, reached by re-slicing the digits, and that code belongs to Northern Mindanao — "
            + "an unrelated region. The digits coincide; the places do not. That proposal was rejected."),

        ("1900000000",
            "The nine-digit edition of the PSGC predates this region. BARMM replaced the Autonomous "
            + "Region in Muslim Mindanao in 2019 following the plebiscite of 21 January and 6 February "
            + "that ratified the Bangsamoro Organic Law. The register's own correspondence points at "
            + "150000000, the ARMM code, but the gazetteer's Autonomous Region in Muslim Mindanao row "
            + "carries no PSGC code at all, so there is nothing on the nine-digit side to pair with."),

        ("1900700000",
            "The register pairs this province with 150700000, Basilan under ARMM, but the gazetteer holds "
            + "'Province of Basilan' without any PSGC code. Basilan is one of the province-level rows the "
            + "gazetteer left uncoded, alongside the complication that Isabela City belongs to Basilan "
            + "while being administered under Region IX. No nine-digit code is available to pair."),

        ("1908703000",
            "Cotabato City has been part of BARMM since the 2019 plebiscite while remaining geographically "
            + "within SOCCSKSARGEN, and the 2Q 2026 register codes it under Maguindanao del Norte. The "
            + "register's correspondence points at 129804000, its old SOCCSKSARGEN code, but the "
            + "gazetteer's 'Cotabato City' row carries no PSGC code, so the pairing has no counterpart."),

        ("1908700000",
            "Maguindanao was divided into Maguindanao del Norte and Maguindanao del Sur in 2022. The "
            + "register publishes no nine-digit correspondence for either half, because the undivided "
            + "province's code cannot belong to one of them without being wrong about the other, and the "
            + "gazetteer's row for this half carries no PSGC code."),

        ("1908800000",
            "Maguindanao was divided into Maguindanao del Norte and Maguindanao del Sur in 2022. The "
            + "register publishes no nine-digit correspondence for either half, because the undivided "
            + "province's code cannot belong to one of them without being wrong about the other, and the "
            + "gazetteer's row for this half carries no PSGC code."),
    ];

    /// <summary>
    /// The eight Special Geographic Area municipalities, which share one history and one reason.
    /// </summary>
    private static readonly string[] SpecialGeographicAreaCodes =
    [
        "1999901000",
        "1999902000",
        "1999903000",
        "1999904000",
        "1999905000",
        "1999906000",
        "1999907000",
        "1999908000",
    ];

    private const string SpecialGeographicAreaReason =
        "A municipality of the Bangsamoro Special Geographic Area, which did not exist when the nine-digit "
        + "PSGC was published and therefore has no code in it. The SGA is 63 barangays of Cotabato "
        + "province that voted to join BARMM in the plebiscite of 6 February 2019 while the province itself "
        + "remained in SOCCSKSARGEN. They were grouped into eight clusters, constituted as municipalities "
        + "by Bangsamoro Autonomy Acts 41 to 48 of 2023, ratified by plebiscite in April 2024, and first "
        + "reflected in the PSGC that year. The gazetteer holds no row for any of them.";

    /// <summary>
    /// Directory rows carrying a nine-digit code that no unit in the register claims.
    /// </summary>
    private static readonly (string DirectoryCode, string Reason)[] DirectoryExceptions =
    [
        ("133901000", ManilaDistrict), ("133902000", ManilaDistrict), ("133903000", ManilaDistrict),
        ("133904000", ManilaDistrict), ("133905000", ManilaDistrict), ("133906000", ManilaDistrict),
        ("133907000", ManilaDistrict), ("133908000", ManilaDistrict), ("133909000", ManilaDistrict),
        ("133910000", ManilaDistrict), ("133911000", ManilaDistrict), ("133912000", ManilaDistrict),
        ("133913000", ManilaDistrict), ("133914000", ManilaDistrict),
        ("137400000", ManilaDistrictGroup),
        ("137500000", ManilaDistrictGroup),
        ("137600000", ManilaDistrictGroup),
    ];

    private const string ManilaDistrict =
        "A district of the City of Manila. The gazetteer records Manila's districts as though they were "
        + "municipalities, but the PSGC classifies them as sub-municipalities below the city, and the "
        + "register holds no city-or-municipality unit for them: the unit is the City of Manila itself. "
        + "Pairing this row with any register unit would assert that a district of Manila is a "
        + "municipality, which is what the gazetteer's own labelling gets wrong. Metro Manila was recoded "
        + "wholesale in the ten-digit register and its digits do not re-slice, so no mechanical pairing is "
        + "available either.";

    private const string ManilaDistrictGroup =
        "A grouping of Manila districts that the gazetteer records at province level. The PSGC has no "
        + "province-level unit inside the National Capital Region — NCR's districts are statistical "
        + "groupings of cities, not provinces — so there is no register unit this row could pair with "
        + "without inventing an administrative tier the country does not have.";

    /// <summary>
    /// Accepts every known exception, reporting each outcome. Already-accepted subjects are not an error.
    /// </summary>
    public static async Task<int> AcceptKnownExceptionsAsync(IDispatcher dispatcher, string acceptedBy)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        Console.WriteLine();
        Console.WriteLine("ADR-005 D5 — ACCEPTING THE UNMATCHED SET");
        Console.WriteLine("========================================");
        Console.WriteLine();
        Console.WriteLine($"  Accepted by        {acceptedBy}");
        Console.WriteLine();

        var accepted = 0;
        var alreadyHeld = 0;
        var failed = 0;

        foreach (var (code, reason) in UnitExceptions)
        {
            var outcome = await AcceptAsync(dispatcher, code, null, reason, acceptedBy);
            Tally(outcome, ref accepted, ref alreadyHeld, ref failed);
        }

        foreach (var code in SpecialGeographicAreaCodes)
        {
            var outcome = await AcceptAsync(
                dispatcher,
                code,
                null,
                SpecialGeographicAreaReason,
                acceptedBy);

            Tally(outcome, ref accepted, ref alreadyHeld, ref failed);
        }

        foreach (var (code, reason) in DirectoryExceptions)
        {
            var outcome = await AcceptAsync(dispatcher, null, code, reason, acceptedBy);
            Tally(outcome, ref accepted, ref alreadyHeld, ref failed);
        }

        Console.WriteLine();
        Console.WriteLine($"  Accepted           {accepted}");
        Console.WriteLine($"  Already held       {alreadyHeld}");
        Console.WriteLine($"  Failed             {failed}");
        Console.WriteLine();

        return failed == 0 ? 0 : 1;
    }

    private static async Task<string> AcceptAsync(
        IDispatcher dispatcher,
        string? canonicalCode,
        string? directoryCode,
        string reason,
        string acceptedBy)
    {
        var subject = canonicalCode ?? directoryCode ?? "unknown";

        var result = await dispatcher.Send(new AcceptLguCrosswalkException.Command(
            canonicalCode,
            directoryCode,
            reason,
            acceptedBy));

        if (result.IsSuccess)
        {
            Console.WriteLine($"  accepted   {subject}");

            return "accepted";
        }

        if (result.Error!.Code == "crosswalk_exception.already_accepted")
        {
            Console.WriteLine($"  held       {subject} (already accepted against this edition)");

            return "held";
        }

        Console.WriteLine($"  FAILED     {subject}: {result.Error.Code} — {result.Error.Description}");

        return "failed";
    }

    private static void Tally(string outcome, ref int accepted, ref int alreadyHeld, ref int failed)
    {
        switch (outcome)
        {
            case "accepted":
                accepted++;
                break;
            case "held":
                alreadyHeld++;
                break;
            default:
                failed++;
                break;
        }
    }
}
