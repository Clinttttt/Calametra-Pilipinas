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
    /// <summary>
    /// Prints what one import changed, so a register edition arriving is a reviewable event rather than a
    /// set of counts that silently replaced the previous ones.
    /// </summary>
    /// <remarks>
    /// Renames, level changes and absences are separated because they mean different things. A rename is
    /// the PSA relabelling a unit that persists; a level change alters what the unit is; an absence may be
    /// a dissolution or the first half of a split, and it is the one an operator must look at by hand.
    /// </remarks>
    public static void PrintImport(ImportPsgcRegister.RegisterImportSummary summary)
    {
        var culture = CultureInfo.InvariantCulture;
        var authority = summary.IsCitableAsAuthority
            ? "MAY certify a crosswalk"
            : "MAY NOT certify a crosswalk";

        Console.WriteLine();
        Console.WriteLine("CANONICAL REGISTER IMPORT");
        Console.WriteLine("=========================");
        Console.WriteLine();
        Console.WriteLine($"  Edition            {summary.EditionLabel}");
        Console.WriteLine($"  Provenance         {summary.Provenance} — {authority}");
        Console.WriteLine($"  Publication date   {summary.PublicationDate?.ToString("yyyy-MM-dd", culture) ?? "not supplied"}");
        Console.WriteLine($"  Original filename  {summary.OriginalFileName ?? "n/a"}");
        Console.WriteLine($"  SHA-256            {summary.FileSha256 ?? "n/a"}");
        Console.WriteLine();
        Console.WriteLine(string.Create(
            culture,
            $"  Composition        {summary.RegionCount} regions, {summary.ProvinceCount} provinces, {summary.CityCount} cities, {summary.MunicipalityCount} municipalities"));
        Console.WriteLine(string.Create(
            culture,
            $"  Cities + munis     {summary.CityCount + summary.MunicipalityCount} — gate 2's review population, derived from this edition"));
        Console.WriteLine();
        Console.WriteLine(string.Create(culture, $"  Units created      {summary.UnitsCreated}"));
        Console.WriteLine(string.Create(culture, $"  Units reconciled   {summary.UnitsReconciled}"));
        Console.WriteLine(string.Create(culture, $"  Units rejected     {summary.UnitsRejected}"));
        Console.WriteLine(string.Create(
            culture,
            $"  Stated pairings    {summary.RegisterStatedPairings} — the register published both editions of the code"));
        Console.WriteLine();
        Console.WriteLine("DELTA AGAINST THE PREVIOUS EDITION");
        Console.WriteLine(string.Create(culture, $"  Renamed            {summary.UnitsRenamed}"));
        Console.WriteLine(string.Create(culture, $"  Level changed      {summary.UnitsLevelChanged}"));
        Console.WriteLine(string.Create(
            culture,
            $"  Absent from new    {summary.UnitsRetiredFromRegister} — retained, not deleted; see the warnings above"));
        Console.WriteLine(string.Create(culture, $"  Editions retired   {summary.EditionsSuperseded}"));
        Console.WriteLine(string.Create(
            culture,
            $"  Proposals retired  {summary.ProposalsSuperseded} — unreviewed only; confirmed and rejected rows untouched"));
        Console.WriteLine();
    }

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
            Console.WriteLine(string.Create(
                culture,
                $"  No CURRENT edition loaded. {report.Register.SupersededEditions} superseded edition(s) on record."));
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

            Console.WriteLine($"  Publication date   {register.PublicationDate?.ToString("yyyy-MM-dd", culture) ?? "not supplied"}");
            Console.WriteLine($"  Original filename  {register.OriginalFileName ?? "n/a — not loaded from a file"}");
            Console.WriteLine($"  SHA-256            {register.FileSha256 ?? "n/a — not loaded from a file"}");
            Console.WriteLine($"  Acquisition        {register.AcquisitionNote ?? "not declared"}");

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
            $"  Register units     {crosswalk.RegisterUnits} in the active edition"));
        Console.WriteLine(string.Create(
            culture,
            $"  Superseded units   {crosswalk.SupersededUnits} held under codes the active edition no longer uses"));
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
        Console.WriteLine(string.Create(
            culture,
            $"  Superseded         {crosswalk.Superseded}  (proposed against a register edition since replaced)"));
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
