using System.Globalization;

using Calametra.Application.Features.Administrative;

namespace Calametra.Ingestion;

/// <summary>
/// Prints what the boundary import acquired and what the coverage check found.
/// </summary>
internal static class LguBoundaryPrinter
{
    public static void WriteImport(ImportLguBoundaries.BoundaryImportSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var culture = CultureInfo.InvariantCulture;

        Console.WriteLine();
        Console.WriteLine("ADR-005 — LGU BOUNDARY IMPORT");
        Console.WriteLine("=============================");
        Console.WriteLine();
        Console.WriteLine($"  Source             {summary.SourceSlug}");
        Console.WriteLine($"  Extracted at       {summary.ExtractedAt:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine($"  Upstream vintage   {summary.ExtractVersion ?? "not stated"}");
        Console.WriteLine();
        Console.WriteLine(string.Create(culture, $"  Relations assembled {summary.RelationsFetched}"));
        Console.WriteLine(string.Create(
            culture,
            $"  Matched on code    {summary.MatchedOnCanonicalCode} on the ten-digit canonical code, {summary.MatchedOnConfirmedCrosswalk} through the confirmed crosswalk"));
        Console.WriteLine();
        Console.WriteLine(string.Create(culture, $"  Stored             {summary.BoundariesStored}"));
        Console.WriteLine(string.Create(
            culture,
            $"  Unchanged          {summary.BoundariesUnchanged} already matched the stored outline and were not re-versioned"));
        Console.WriteLine(string.Create(
            culture,
            $"  Superseded         {summary.BoundariesSuperseded} previous version(s) retired and retained"));
        Console.WriteLine(string.Create(
            culture,
            $"  Repaired           {summary.Repaired} stored after a recorded repair"));
        Console.WriteLine();

        WriteList("UNASSEMBLED RELATIONS", summary.UnassembledRelations, 12);
        WriteList("UNMATCHED RELATIONS", summary.UnmatchedRelations, 12);
        WriteList("AMBIGUOUS UNITS (nothing stored)", summary.AmbiguousUnits, 20);
        WriteList("REFUSED BY THE DOMAIN", summary.RejectedByDomain, 12);
        WriteList("CHUNKS NEVER FETCHED", summary.FailedChunks, 20);
    }

    public static void WriteCanonicalImport(ImportCodAbBoundaries.CodAbImportSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var culture = CultureInfo.InvariantCulture;

        Console.WriteLine();
        Console.WriteLine("ADR-005 D2 — CANONICAL ON-LAND GEOMETRY IMPORT");
        Console.WriteLine("==============================================");
        Console.WriteLine();
        Console.WriteLine($"  Source             {summary.SourceSlug}");
        Console.WriteLine($"  Extract            {summary.ExtractLabel}");
        Console.WriteLine($"  File               {summary.OriginalFileName}");
        Console.WriteLine($"  SHA-256            {summary.FileSha256}");
        Console.WriteLine($"  Vintage            {summary.Vintage?.ToString("yyyy-MM-dd", culture) ?? "not stated"}");
        Console.WriteLine();
        Console.WriteLine(string.Create(culture, $"  Features read      {summary.FeaturesRead}"));
        Console.WriteLine(string.Create(
            culture,
            $"  Matched            {summary.MatchedOnCanonicalCode} on the current canonical code, {summary.MatchedOnCorrespondence} through confirmed edition correspondence"));
        Console.WriteLine();
        Console.WriteLine(string.Create(culture, $"  Stored             {summary.Stored}"));
        Console.WriteLine(string.Create(culture, $"  Unchanged          {summary.Unchanged}"));
        Console.WriteLine(string.Create(
            culture,
            $"  Superseded         {summary.Superseded} previous outline(s) retired and retained"));
        Console.WriteLine(string.Create(culture, $"  Repaired           {summary.Repaired}"));
        Console.WriteLine();

        WriteList("UNMATCHED FEATURES (never name-matched)", summary.UnmatchedFeatures, 20);
        WriteList("AMBIGUOUS UNITS (nothing stored)", summary.AmbiguousUnits, 20);
        WriteList("REJECTED BY THE READER", summary.RejectedByReader, 12);
        WriteList("REFUSED BY THE DOMAIN", summary.RejectedByDomain, 12);
    }

    public static void WriteCoverage(GetLguBoundaryCoverage.BoundaryCoverageReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var culture = CultureInfo.InvariantCulture;

        Console.WriteLine();
        Console.WriteLine("ADR-005 — BOUNDARY COVERAGE");
        Console.WriteLine("===========================");
        Console.WriteLine();

        if (!report.AnyBoundariesHeld)
        {
            Console.WriteLine($"  {report.SufficiencyVerdict}");

            return;
        }

        Console.WriteLine($"  Source             {report.SourceSlug}");
        Console.WriteLine($"  Attribution        {report.Attribution}");
        Console.WriteLine($"  Extracted at       {report.ExtractedAt:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine($"  Upstream vintage   {report.ExtractVersion ?? "not stated"}");
        Console.WriteLine();
        Console.WriteLine(string.Create(culture, $"  Outlines in force  {report.BoundariesInForce}"));
        Console.WriteLine(string.Create(
            culture,
            $"  Superseded         {report.BoundariesSuperseded} retained with their validity period"));
        Console.WriteLine(string.Create(culture, $"  Repaired           {report.BoundariesRepaired}"));
        Console.WriteLine();
        Console.WriteLine("PROVENANCE OF THE OUTLINES IN FORCE");
        Console.WriteLine();

        foreach (var extract in report.Extracts)
        {
            Console.WriteLine(string.Create(
                culture,
                $"  {extract.OutlinesInForce,5} outline(s)  {extract.Label}"));
            Console.WriteLine($"        provenance   {extract.Provenance}");
            Console.WriteLine($"        file         {extract.OriginalFileName ?? "n/a — read from an API"}");
            Console.WriteLine($"        SHA-256      {extract.FileSha256 ?? "n/a — no single set of bytes"}");

            if (extract.FileSizeBytes is { } bytes)
            {
                Console.WriteLine(string.Create(culture, $"        size         {bytes / (1024 * 1024)} MB"));
            }

            Console.WriteLine($"        vintage      {extract.Vintage?.ToString("yyyy-MM-dd", culture) ?? "not stated"}");
            Console.WriteLine($"        acquired     {extract.AcquiredAt:yyyy-MM-dd HH:mm:ss} UTC");
            Console.WriteLine($"        acquisition  {extract.AcquisitionNote ?? "not declared"}");
            Console.WriteLine();
        }

        Console.WriteLine("COVERAGE BY LEVEL, against the active register edition");
        Console.WriteLine();

        foreach (var level in report.Levels)
        {
            Console.WriteLine(string.Create(
                culture,
                $"  {level.Level,-14} {level.UnitsWithBoundary,5} of {level.UnitsInRegister,5}  {level.CoverageRatio,7:P1}   {level.Repaired} repaired"));
        }

        Console.WriteLine();
        Console.WriteLine("GEOMETRY SANITY");
        Console.WriteLine("  Areas are JURISDICTIONAL, not land: the OSM Philippine convention maps a");
        Console.WriteLine("  municipality out to its municipal waters, 15 km from the coastline (RA 8550),");
        Console.WriteLine("  so an island municipality's outline is mostly sea.");
        Console.WriteLine(string.Create(
            culture,
            $"  Total area         {report.TotalAreaSquareKm:N0} km2 of jurisdiction"));
        Console.WriteLine(string.Create(
            culture,
            $"  Smallest outline   {report.SmallestAreaSquareKm:N2} km2"));
        Console.WriteLine(string.Create(
            culture,
            $"  Largest outline    {report.LargestAreaSquareKm:N0} km2  {report.LargestUnitName}"));
        Console.WriteLine();

        if (report.MissingUnits.Count > 0)
        {
            Console.WriteLine(string.Create(
                culture,
                $"UNITS WITH NO OUTLINE ({report.MissingUnits.Count})"));
            Console.WriteLine("  'excepted' marks a unit already carrying a written crosswalk exception.");
            Console.WriteLine();

            foreach (var unit in report.MissingUnits.Take(40))
            {
                var flag = unit.IsExcepted ? "  [excepted]" : string.Empty;
                Console.WriteLine($"  {unit.CanonicalCode}  {unit.Name} ({unit.Level}){flag}");
            }

            if (report.MissingUnits.Count > 40)
            {
                Console.WriteLine(string.Create(
                    culture,
                    $"  ... and {report.MissingUnits.Count - 40} more"));
            }

            Console.WriteLine();
        }

        WriteList("UNITS WITH MORE THAN ONE OUTLINE IN FORCE", report.UnitsWithMultipleVersions, 20);

        Console.WriteLine(report.SufficientForInteraction
            ? "  VERDICT: coverage is sufficient to move to the MapLibre interaction phase."
            : "  VERDICT: coverage is NOT sufficient. The interaction phase must not begin.");
        Console.WriteLine();
        Console.WriteLine($"  {report.SufficiencyVerdict}");
        Console.WriteLine();
    }

    private static void WriteList(string heading, IReadOnlyList<string> items, int limit)
    {
        if (items.Count == 0)
        {
            return;
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{heading} ({items.Count})"));
        Console.WriteLine();

        foreach (var item in items.Take(limit))
        {
            Console.WriteLine($"  {item}");
        }

        if (items.Count > limit)
        {
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  ... and {items.Count - limit} more"));
        }

        Console.WriteLine();
    }
}
