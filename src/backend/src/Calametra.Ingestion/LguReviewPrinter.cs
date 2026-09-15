using System.Globalization;

using Calametra.Application.Features.Administrative;

namespace Calametra.Ingestion;

/// <summary>
/// Prints the review queue and the outcome of a class confirmation.
/// </summary>
/// <remarks>
/// Printed rather than logged. A reviewer is the audience, the decision is theirs, and a work queue
/// buried in structured log output is a work queue nobody reads.
/// </remarks>
internal static class LguReviewPrinter
{
    public static void WriteQueue(GetLguReviewQueue.ReviewQueue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);

        var culture = CultureInfo.InvariantCulture;

        Console.WriteLine();
        Console.WriteLine("ADR-005 — LGU CROSSWALK REVIEW QUEUE");
        Console.WriteLine("====================================");
        Console.WriteLine();

        if (!queue.AnyCurrentEdition)
        {
            Console.WriteLine("  No current register edition. Import one before reviewing.");

            return;
        }

        Console.WriteLine($"  Edition            {queue.EditionLabel}");
        Console.WriteLine(
            $"  May certify        {(queue.EditionMayCertify ? "yes — PSA-direct" : "NO — confirmation will be refused")}");
        Console.WriteLine();
        Console.WriteLine(string.Create(culture, $"  Proposed           {queue.Proposed}"));
        Console.WriteLine(string.Create(culture, $"  Confirmed          {queue.Confirmed}"));
        Console.WriteLine(string.Create(culture, $"  Rejected           {queue.Rejected}"));
        Console.WriteLine();

        Console.WriteLine("EVIDENCE CLASSES");
        Console.WriteLine();

        foreach (var item in queue.Classes)
        {
            Console.WriteLine(string.Create(
                culture,
                $"  {item.Evidence,-14} {item.Count,6} row(s)   {(item.BatchConfirmable ? "may be confirmed as a named batch" : "individual review only")}"));
            Console.WriteLine($"                 {item.WhyItCanBeBatched}");
            Console.WriteLine();
        }

        if (queue.RequiringIndividualReview.Count > 0)
        {
            Console.WriteLine("REQUIRING INDIVIDUAL REVIEW");
            Console.WriteLine("  Both names are shown because the disagreement is the reason these are here.");
            Console.WriteLine();

            foreach (var item in queue.RequiringIndividualReview)
            {
                Console.WriteLine(
                    $"  {item.CanonicalCode} -> {item.HistoricalCode}  {item.Evidence}, names agree: {item.NamesAgree}");
                Console.WriteLine($"    register  {item.UnitName}");
                Console.WriteLine($"    directory {item.DirectoryName ?? "no row paired"}");
                Console.WriteLine($"    basis     {item.Basis}");
                Console.WriteLine($"    link id   {item.LinkId}");
                Console.WriteLine();
            }
        }

        if (queue.UnitsWithoutProposal.Count > 0)
        {
            Console.WriteLine(string.Create(
                culture,
                $"REGISTER UNITS WITH NO CANDIDATE ({queue.UnitsWithoutProposal.Count}) — need an accepted exception, not a review"));
            Console.WriteLine();

            foreach (var unit in queue.UnitsWithoutProposal)
            {
                Console.WriteLine($"  {unit.CanonicalCode}  {unit.Name} ({unit.Level})");
            }

            Console.WriteLine();
        }

        if (queue.DirectoryRowsWithoutProposal.Count > 0)
        {
            Console.WriteLine(string.Create(
                culture,
                $"DIRECTORY ROWS WITH NO CANDIDATE ({queue.DirectoryRowsWithoutProposal.Count})"));
            Console.WriteLine();

            foreach (var row in queue.DirectoryRowsWithoutProposal)
            {
                Console.WriteLine($"  {row.PsgcCode}  {row.Name} ({row.Kind})");
            }

            Console.WriteLine();
        }

        Console.WriteLine(string.Create(
            culture,
            $"  Already excepted   {queue.UnitsAlreadyExcepted} units, {queue.DirectoryRowsAlreadyExcepted} directory rows"));
        Console.WriteLine();
    }

    public static void WriteClassConfirmation(
        ConfirmLguCodeLinkClass.ClassConfirmationSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var culture = CultureInfo.InvariantCulture;

        Console.WriteLine();
        Console.WriteLine("EVIDENCE CLASS CONFIRMED — NAMED, DATED BATCH (ADR-005 D4)");
        Console.WriteLine("==========================================================");
        Console.WriteLine();
        Console.WriteLine($"  Evidence class     {summary.Evidence}");
        Console.WriteLine($"  Edition cited      {summary.EditionLabel}");
        Console.WriteLine($"  Reviewed by        {summary.ReviewedBy}");
        Console.WriteLine($"  Reviewed at        {summary.ReviewedAt:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine();
        Console.WriteLine(string.Create(culture, $"  Confirmed          {summary.Confirmed}"));
        Console.WriteLine(string.Create(
            culture,
            $"  Refused           {summary.Refused} — left proposed; the domain declined them and the batch did not override it"));

        foreach (var reason in summary.RefusalReasons.Take(20))
        {
            Console.WriteLine($"      {reason}");
        }

        Console.WriteLine();
    }
}
