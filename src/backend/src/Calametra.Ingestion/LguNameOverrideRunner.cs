using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.Administrative;

namespace Calametra.Ingestion;

/// <summary>
/// The four units COD-AB still names by a name the PSA has replaced.
/// </summary>
/// <remarks>
/// <para>
/// <b>Four, and no more.</b> Every other code-with-disagreeing-name is still refused, because that
/// disagreement is exactly what caught sixteen Maguindanao outlines attaching to the wrong municipality on
/// exact code equality. What follows is not a relaxation of that rule but four named exceptions to it, each
/// stating the evidence that the publisher's name is superseded rather than a different place.
/// </para>
/// <para>
/// Two of the four are self-evidencing: COD-AB itself carries the register's current name in parentheses
/// beside the former one. The other two rest on a PSA renaming and on the register using the full official
/// form where the publisher keeps the short one.
/// </para>
/// </remarks>
internal static class LguNameOverrideRunner
{
    private static readonly (string Code, string SourceName, string Reason)[] Cases =
    [
        ("0907226000", "Bacungan (Leon T. Postigo)",
            "Zamboanga del Norte. COD-AB names this unit 'Bacungan (Leon T. Postigo)' — it carries the "
            + "register's current name, 'Leon T. Postigo', in parentheses beside the former name Bacungan, "
            + "so the publisher itself records the two as one municipality. The PSA corrected the name in a "
            + "quarterly PSGC update. Same code, same province, one place under two names."),

        ("1004217000", "Don Victoriano Chiongbian (Don Mariano Marcos)",
            "Misamis Occidental. COD-AB names this unit 'Don Victoriano Chiongbian (Don Mariano Marcos)'; "
            + "the register writes the short form 'Don Victoriano'. The publisher's primary name is the full "
            + "official one and the parenthesis records the earlier name Don Mariano Marcos. Same code, same "
            + "province, and the register's name is a contraction of the publisher's rather than a different "
            + "municipality."),

        ("1102324000", "San Isidro",
            "Davao del Norte. The PSA 2Q 2026 PSGC update renamed the Municipality of San Isidro to the "
            + "Municipality of Sawata, and renamed its Barangay Sawata to Barangay Poblacion. COD-AB "
            + "predates that update and still publishes 'San Isidro' at this code. The register's 'Sawata' "
            + "and the publisher's 'San Isidro' are the same municipality before and after the renaming."),

        ("1705323000", "Rizal (Marcos)",
            "Palawan. COD-AB names this unit 'Rizal (Marcos)', recording Marcos as its earlier name; the "
            + "register writes the fuller official form 'Dr. Jose P. Rizal'. Same code, same province, and "
            + "the publisher's 'Rizal' is the short form of the register's name rather than a different "
            + "municipality."),
    ];

    public static async Task<int> ConfirmAsync(IDispatcher dispatcher, string reviewedBy)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        Console.WriteLine();
        Console.WriteLine("REVIEWED SOURCE-NAME OVERRIDES (four documented renamings)");
        Console.WriteLine("=========================================================");
        Console.WriteLine();
        Console.WriteLine($"  Reviewed by        {reviewedBy}");
        Console.WriteLine();

        var confirmed = 0;
        var refused = 0;

        foreach (var (code, sourceName, reason) in Cases)
        {
            var result = await dispatcher.Send(new ConfirmLguSourceNameOverride.Command(
                code,
                sourceName,
                reviewedBy,
                reason));

            if (result.IsSuccess)
            {
                Console.WriteLine($"  confirmed  {code}  '{sourceName}'");
                confirmed++;
            }
            else if (result.Error!.Code == "source_name_override.already_held")
            {
                Console.WriteLine($"  held       {code}  '{sourceName}'");
            }
            else
            {
                Console.WriteLine($"  REFUSED    {code}: {result.Error.Code}");
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
