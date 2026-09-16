using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.Administrative;

namespace Calametra.Ingestion;

/// <summary>
/// The correspondences the mechanical evidence did not settle, each with the reason that justifies it.
/// </summary>
/// <remarks>
/// <para>
/// <b>These reasons are the deliverable.</b> Every pairing below is a person's decision recorded in their
/// words, because the register published no nine-digit correspondence that could establish it. The reasons
/// are held to the standard of published text: what is stated is checkable, and where it is not certain the
/// pairing is left out rather than written down as though it were.
/// </para>
/// <para>
/// Held in the ingestion host rather than the application layer because these are findings about two
/// specific editions at one moment, not rules. A later PSA publication may state the correspondence
/// outright, and when it does the finding changes while the code that enforces review does not.
/// </para>
/// </remarks>
internal static class LguManualCorrespondenceRunner
{
    /// <summary>
    /// The boundary set codes the City of Manila as one of its own districts.
    /// </summary>
    private static readonly (string Legacy, string Current, string Reason)[] Cases =
    [
        ("1303901000", "1380600000",
            "The boundary set codes the City of Manila 1303901000, whose nine-digit re-slice is 133901000 — "
            + "which is Tondo I/II, one district of the city and a sub-city unit this platform already holds "
            + "as an accepted crosswalk exception. The register confirms the city itself as 1380600000 "
            + "against 133900000. The set contains exactly one Manila city polygon and no district polygons "
            + "at this level, so the outline is the city's; only its code is irregular. Confirmed as the City "
            + "of Manila and explicitly NOT as Tondo."),

        // Maguindanao del Norte. The province was created in 2022 and the PSA renumbered its municipalities
        // without publishing a nine-digit correspondence for either half, so no bridge can reach these.
        // Each pairing below rests on the municipality name being identical and the parent province being the
        // same in both editions.
        ("1908714000", "1908706000",
            "Kabuntalan, Maguindanao del Norte. The boundary set writes 'Kabuntalan (Tumbao)', carrying the "
            + "former name in parentheses as it does throughout; the register writes 'Kabuntalan'. Same "
            + "municipality, same province, renumbered within Maguindanao del Norte after the 2022 division, "
            + "for which the PSA published no nine-digit correspondence."),

        ("1908715000", "1908713000",
            "Upi, Maguindanao del Norte. Identical name and parent province in both editions; renumbered "
            + "within the province after the 2022 division, which the PSA published no nine-digit "
            + "correspondence for."),

        ("1908718000", "1908701000",
            "Barira, Maguindanao del Norte. Identical name and parent province in both editions; renumbered "
            + "within the province after the 2022 division, which the PSA published no nine-digit "
            + "correspondence for."),

        ("1908730000", "1908704000",
            "Datu Blah T. Sinsuat, Maguindanao del Norte. Identical name and parent province in both "
            + "editions; renumbered within the province after the 2022 division, which the PSA published no "
            + "nine-digit correspondence for."),

        ("1908734000", "1908708000",
            "Northern Kabuntalan, Maguindanao del Norte. Identical name and parent province in both "
            + "editions; renumbered within the province after the 2022 division, which the PSA published no "
            + "nine-digit correspondence for."),

        // Maguindanao del Sur.
        ("1908825000", "1908812000",
            "Guindulungan, Maguindanao del Sur. Identical name and parent province in both editions; "
            + "renumbered within the province after the 2022 division, which the PSA published no nine-digit "
            + "correspondence for."),

        ("1908826000", "1908809000",
            "Datu Saudi Ampatuan, Maguindanao del Sur. The boundary set hyphenates it 'Datu "
            + "Saudi-Ampatuan'; the register does not. Same municipality, same province, renumbered within "
            + "the province after the 2022 division, which the PSA published no nine-digit correspondence "
            + "for."),

        ("1908831000", "1908804000",
            "Datu Anggal Midtimbang, Maguindanao del Sur. Identical name and parent province in both "
            + "editions; renumbered within the province after the 2022 division, which the PSA published no "
            + "nine-digit correspondence for."),

        ("1908832000", "1908814000",
            "Mangudadatu, Maguindanao del Sur. Identical name and parent province in both editions; "
            + "renumbered within the province after the 2022 division, which the PSA published no nine-digit "
            + "correspondence for."),

        ("1908833000", "1908818000",
            "Pandag, Maguindanao del Sur. Identical name and parent province in both editions; renumbered "
            + "within the province after the 2022 division, which the PSA published no nine-digit "
            + "correspondence for."),

        ("1908837000", "1908821000",
            "Shariff Saydona Mustapha, Maguindanao del Sur. Identical name and parent province in both "
            + "editions; renumbered within the province after the 2022 division, which the PSA published no "
            + "nine-digit correspondence for."),
    ];

    public static async Task<int> ConfirmAsync(IDispatcher dispatcher, string reviewedBy)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        Console.WriteLine();
        Console.WriteLine("ADR-005 D4 — MANUAL EDITION CORRESPONDENCE");
        Console.WriteLine("==========================================");
        Console.WriteLine();
        Console.WriteLine($"  Reviewed by        {reviewedBy}");
        Console.WriteLine();

        var confirmed = 0;
        var refused = 0;

        foreach (var (legacy, current, reason) in Cases)
        {
            var result = await dispatcher.Send(new ConfirmLguEditionCorrespondence.Command(
                legacy,
                current,
                reviewedBy,
                reason));

            if (result.IsSuccess)
            {
                Console.WriteLine($"  confirmed  {legacy} -> {current}");
                confirmed++;
            }
            else
            {
                Console.WriteLine($"  REFUSED    {legacy} -> {current}: {result.Error!.Code}");
                refused++;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"  Confirmed          {confirmed}");
        Console.WriteLine($"  Refused            {refused}");
        Console.WriteLine();

        return refused == 0 ? 0 : 1;
    }
}
