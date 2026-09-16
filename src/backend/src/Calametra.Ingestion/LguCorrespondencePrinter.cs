using System.Globalization;

using Calametra.Application.Features.Administrative;

namespace Calametra.Ingestion;

internal static class LguCorrespondencePrinter
{
    public static void WriteProposal(
        ProposeLguEditionCorrespondences.CorrespondenceProposalSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var culture = CultureInfo.InvariantCulture;

        Console.WriteLine();
        Console.WriteLine("ADR-005 D4 — EDITION CORRESPONDENCE PROPOSED");
        Console.WriteLine("============================================");
        Console.WriteLine();
        Console.WriteLine($"  Current edition    {summary.EditionLabel}");
        Console.WriteLine();
        Console.WriteLine(string.Create(culture, $"  Legacy codes read  {summary.LegacyCodesExamined}"));
        Console.WriteLine(string.Create(
            culture,
            $"  Already current    {summary.AlreadyCurrent} — the active edition still uses these codes, so no correspondence is needed"));
        Console.WriteLine(string.Create(culture, $"  Already held       {summary.AlreadyHeld}"));
        Console.WriteLine(string.Create(culture, $"  Proposed           {summary.Proposed}"));
        Console.WriteLine();
        Console.WriteLine("EVIDENCE OF THE PROPOSALS");
        Console.WriteLine(string.Create(
            culture,
            $"  Register bridge    {summary.RegisterBridge} — a unit this platform held under the earlier edition supplied the nine-digit code"));
        Console.WriteLine(string.Create(
            culture,
            $"  Reslice only       {summary.ResliceOnly} — reached only by the published re-slice, then a confirmed pairing"));
        Console.WriteLine(string.Create(
            culture,
            $"  Bridges disagree   {summary.BridgesDisagree} — cannot be confirmed mechanically"));
        Console.WriteLine(string.Create(culture, $"  Names agree        {summary.NamesAgree}"));
        Console.WriteLine(string.Create(
            culture,
            $"  Names disagree     {summary.NamesDisagree} — individual review only"));
        Console.WriteLine();

        if (summary.Unreachable.Count > 0)
        {
            Console.WriteLine(string.Create(
                culture,
                $"UNREACHABLE ({summary.Unreachable.Count}) — no confirmed nine-digit pairing by either bridge"));
            Console.WriteLine();

            foreach (var item in summary.Unreachable.Take(30))
            {
                Console.WriteLine($"  {item}");
            }

            if (summary.Unreachable.Count > 30)
            {
                Console.WriteLine(string.Create(
                    culture,
                    $"  ... and {summary.Unreachable.Count - 30} more"));
            }

            Console.WriteLine();
        }
    }

    public static void WriteClassConfirmation(
        ConfirmLguEditionCorrespondenceClass.CorrespondenceConfirmationSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var culture = CultureInfo.InvariantCulture;

        Console.WriteLine();
        Console.WriteLine("EDITION CORRESPONDENCE CONFIRMED — NAMED, DATED BATCH (ADR-005 D4)");
        Console.WriteLine("==================================================================");
        Console.WriteLine();
        Console.WriteLine($"  Edition cited      {summary.EditionLabel}");
        Console.WriteLine($"  Reviewed by        {summary.ReviewedBy}");
        Console.WriteLine($"  Reviewed at        {summary.ReviewedAt:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine();
        Console.WriteLine(string.Create(culture, $"  Confirmed          {summary.Confirmed}"));
        Console.WriteLine(string.Create(
            culture,
            $"  Refused            {summary.Refused} — left proposed; the domain declined them"));

        foreach (var reason in summary.RefusalReasons.Take(20))
        {
            Console.WriteLine($"      {reason}");
        }

        Console.WriteLine();
    }
}
