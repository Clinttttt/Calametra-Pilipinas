using System.Globalization;
using Calametra.Application.Features.Administrative;

namespace Calametra.Ingestion;

/// <summary>
/// Prints the ADR-005 gate report to the console.
/// </summary>
/// <remarks>
/// <para>
/// A printer rather than a log message, because this report is an artefact a person reads before
/// deciding whether geometry work may begin, and structured logging is optimised for machines reading
/// one line at a time. The process also exits non-zero while the gate is shut, so a script cannot
/// proceed by ignoring the text.
/// </para>
/// <para>
/// Every figure here is measured from the database by the query. Nothing in this file decides anything;
/// it formats a verdict it was given.
/// </para>
/// </remarks>
internal static class LguReadinessPrinter
{
    public static void Write(GetLguCrosswalkReadiness.ReadinessReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var culture = CultureInfo.InvariantCulture;

        Console.WriteLine();
        Console.WriteLine("ADR-005 — LGU CROSSWALK READINESS");
        Console.WriteLine("=================================");
        Console.WriteLine();

        Console.WriteLine("THE FOUR CONDITIONS");
        foreach (var condition in report.Conditions)
        {
            Console.WriteLine(string.Create(
                culture,
                $"  {(condition.Met ? "PASS" : "FAIL")}  {condition.Number}. {condition.Name}"));
            Console.WriteLine($"        {condition.Detail}");
        }

        Console.WriteLine();
        Console.WriteLine(report.MayBeginGeometryIngestion
            ? "  VERDICT: the gate is OPEN. Polygon ingestion may begin."
            : "  VERDICT: the gate is SHUT. Polygon ingestion must not begin.");

        Console.WriteLine();
        Console.WriteLine("REGISTER");

        if (!report.Register.AnyEditionLoaded)
        {
            Console.WriteLine("  No edition loaded.");
        }
        else
        {
            var register = report.Register;

            Console.WriteLine($"  Edition            {register.Label}");
            Console.WriteLine(string.Create(
                culture,
                $"  Provenance         {register.Provenance} — "
                + $"{(register.IsCitableAsAuthority ? "may certify a crosswalk" : "MAY NOT certify a crosswalk")}"));
            Console.WriteLine(string.Create(
                culture,
                $"  Observed           {register.RegionCount} regions, {register.ProvinceCount} provinces, "
                + $"{register.CityCount} cities, {register.MunicipalityCount} municipalities"));
            Console.WriteLine(string.Create(
                culture,
                $"  Upstream modified  {register.UpstreamLastModified?.ToString("yyyy-MM-dd", culture) ?? "not reported"}"));

            if (register.Notes is not null)
            {
                Console.WriteLine($"  Notes              {register.Notes}");
            }
        }

        var crosswalk = report.Crosswalk;

        Console.WriteLine();
        Console.WriteLine("CROSSWALK");
        Console.WriteLine(string.Create(
            culture,
            $"  Register units     {crosswalk.RegisterUnits}"));
        Console.WriteLine(string.Create(
            culture,
            $"  Directory rows     {crosswalk.DirectoryRowsWithCode} carrying a nine-digit code"));
        Console.WriteLine(string.Create(
            culture,
            $"  Proposed           {crosswalk.Proposed}  (unreadable by analytics, by construction)"));
        Console.WriteLine(string.Create(
            culture,
            $"  Confirmed          {crosswalk.Confirmed}  "
            + $"[register match {crosswalk.ConfirmedOnRegisterMatch}, "
            + $"re-slice {crosswalk.ConfirmedOnDigitReslice}, "
            + $"manual {crosswalk.ConfirmedOnManualReview}]"));
        Console.WriteLine(string.Create(culture, $"  Rejected           {crosswalk.Rejected}"));
        var rate = report.ProposalRejectionRate;

        // Formatted before interpolation: a null rate is "not measurable", which is a different claim
        // from 0% and must not be rendered as one.
        var rateText = rate is null
            ? "not measurable — nothing reviewed yet"
            : rate.Value.ToString("P1", culture);

        Console.WriteLine($"  Rejection rate     {rateText}");
        Console.WriteLine(string.Create(
            culture,
            $"  Blocked proposals  {crosswalk.ProposalsBlockedByNameDisagreement} re-slicings whose names disagree; the domain refuses to confirm these on that evidence"));

        Console.WriteLine();
        Console.WriteLine("UNMATCHED SET (enumerated, not driven to zero)");
        Console.WriteLine(string.Create(
            culture,
            $"  Register units without a confirmed pairing   {report.UnmatchedRegisterUnits}"));
        Console.WriteLine(string.Create(
            culture,
            $"  Directory rows without a confirmed pairing   {report.UnmatchedDirectoryRows}"));

        Console.WriteLine();
        Console.WriteLine("READ BOUNDARY");
        Console.WriteLine(string.Create(
            culture,
            $"  Layer 1 view       {(report.ReadBoundary.ConfirmedOnlyViewPresent ? "present" : "MISSING")}"));
        Console.WriteLine(string.Create(
            culture,
            $"  Leakage            {(report.ReadBoundary.ProposalsVisibleThroughAnalyticsSurface ? "PROPOSALS ARE VISIBLE — STOP" : "none: analytics see confirmed rows only")}"));
        Console.WriteLine(string.Create(
            culture,
            $"  Layer 2 grants     {report.ReadBoundary.DatabasePermissionsActive?.ToString() ?? "not measurable from the application's own connection"}"));
        Console.WriteLine($"  {report.ReadBoundary.Detail}");
        Console.WriteLine();
    }
}
