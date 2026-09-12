using Calametra.Domain.Geospatial;
using Calametra.Domain.Seismology;
using Calametra.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Calametra.Infrastructure.Persistence.Seed;

/// <summary>
/// Records published agency bulletin figures for reference events.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Calametra ingests the USGS catalogue programmatically, so
/// every event starts with exactly one observation. That makes the platform's central
/// argument — that one earthquake has several correct magnitudes — impossible to
/// demonstrate, because there is nothing to compare against.
/// </para>
/// <para>
/// <b>Why this is not a licensing problem.</b> This is citation, not dataset
/// redistribution. PHIVOLCS publishes individual earthquake bulletins openly on its
/// website; recording the figures from a named, dated, linked bulletin is the same
/// act as quoting it in a paper. It is categorically different from copying their
/// GIS vector layers, which is what the pending Data User Agreement governs and
/// which Calametra does not do.
/// </para>
/// <para>
/// <b>Why it is a separate source.</b> The bulletin source is registered distinctly
/// from the GIS services with <see cref="SourceAccessKind.ManualImport"/>, so the
/// About Data page can state plainly that these figures were transcribed by hand
/// from published bulletins rather than fetched from an API. A reader can then judge
/// them accordingly, and nothing pretends to be automated that is not.
/// </para>
/// <para>
/// Kept deliberately small. This is a reference-event mechanism, not a substitute
/// for the local catalogue: transcribing thousands of bulletins by hand would be
/// neither honest nor maintainable.
/// </para>
/// </remarks>
public sealed class BulletinObservationSeeder(
    ApplicationDbContext context,
    TimeProvider timeProvider,
    ILogger<BulletinObservationSeeder> logger)
{
    /// <summary>Slug of the published-bulletin source.</summary>
    public const string BulletinSourceSlug = "phivolcs-earthquake-bulletin";

    /// <summary>
    /// A figure published in an agency bulletin, to be attached to a matching event.
    /// </summary>
    private sealed record BulletinRecord
    {
        public required string BulletinId { get; init; }

        public required DateTimeOffset OccurredAt { get; init; }

        public required double Latitude { get; init; }

        public required double Longitude { get; init; }

        /// <summary>
        /// Depth as the bulletin prints it, or null when the bulletin publishes none.
        /// </summary>
        /// <remarks>
        /// Nullable because two of the transcribed events genuinely have no PHIVOLCS depth. The 1976
        /// Moro Gulf page publishes an epicentre and a magnitude and no depth at all; the 1994 Mindoro
        /// special report gives "7 to 12 kilometres" but attributes it to Prof. Kikuchi of Yokohama
        /// University rather than to the PHIVOLCS network, so recording it as PHIVOLCS's reading would
        /// misattribute somebody else's work. Recorded as <see cref="DepthReading.Unknown"/>, which the
        /// interface already knows how to present, rather than filled with a plausible number.
        /// </remarks>
        public double? DepthKm { get; init; }

        public required double Magnitude { get; init; }

        public required MagnitudeType Scale { get; init; }

        public required string SourceUrl { get; init; }

        /// <summary>How far from the matching event this may be, in kilometres.</summary>
        public double MatchRadiusKm { get; init; } = 60d;

        /// <summary>How far from the matching event's origin time this may be.</summary>
        public TimeSpan MatchWindow { get; init; } = TimeSpan.FromMinutes(3);
    }

    /// <summary>
    /// Reference events. Each figure is transcribed from the linked public bulletin.
    /// </summary>
    private static readonly BulletinRecord[] Bulletins =
    [
        new()
        {
            // The 10 February 2017 Surigao earthquake. PHIVOLCS reports Ms 6.7 at
            // 10 km depth; USGS reports the same event as Mww 6.5 at 15 km
            // (us20008ixa). Ms and Mww belong to different scale families, so the
            // 0.2 difference is not a discrepancy to reconcile — the two numbers
            // measure different things.
            BulletinId = "2017_0210_1403",
            OccurredAt = new DateTimeOffset(2017, 2, 10, 14, 3, 43, TimeSpan.Zero),
            Latitude = 9.93d,
            Longitude = 125.45d,
            DepthKm = 10d,
            Magnitude = 6.7d,
            Scale = MagnitudeType.Ms,
            SourceUrl =
                "https://earthquake.phivolcs.dost.gov.ph/2017_Earthquake_Information/February/2017_0210_1403_B4F.html",
        },

        new()
        {
            // 2 December 2023, offshore Hinatuan, Surigao del Sur. The event a reader is most likely
            // to arrive looking for, and the clearest case for this whole mechanism: PHIVOLCS calls it
            // Mw 7.4 at 26 km, USGS calls the same rupture Mww 7.6 at 32 km (us7000lff4), and the
            // epicentres are about 21 km apart. Searching for "M7.4 Hinatuan" finds nothing in a
            // USGS-only archive.
            BulletinId = "2023_1202_1437",
            OccurredAt = new DateTimeOffset(2023, 12, 2, 14, 37, 5, TimeSpan.Zero),
            Latitude = 8.44d,
            Longitude = 126.59d,
            DepthKm = 26d,
            Magnitude = 7.4d,
            Scale = MagnitudeType.Mw,
            SourceUrl =
                "https://earthquake.phivolcs.dost.gov.ph/2023_Earthquake_Information/December/2023_1202_1437_B3F.html",
        },

        new()
        {
            // 3 August 2024 local time, Lingig, Surigao del Sur. Both agencies say 6.8 — and the depth
            // does not agree at all: PHIVOLCS 11 km against USGS 32 km, which is the difference between
            // a shallow crustal rupture and one well inside the slab.
            BulletinId = "2024_0802_2222",
            OccurredAt = new DateTimeOffset(2024, 8, 2, 22, 22, 57, TimeSpan.Zero),
            Latitude = 8.12d,
            Longitude = 126.84d,
            DepthKm = 11d,
            Magnitude = 6.8d,
            Scale = MagnitudeType.Mw,
            SourceUrl =
                "https://earthquake.phivolcs.dost.gov.ph/2024_Earthquake_Information/August/2024_0802_2222_B6F.html",
        },

        new()
        {
            // 10 October 2025, Manay, Davao Oriental. Magnitudes agree at 7.4; PHIVOLCS puts it 36 km
            // shallower than USGS, at 23 km against 59 km.
            BulletinId = "2025_1010_0143",
            OccurredAt = new DateTimeOffset(2025, 10, 10, 1, 43, 55, TimeSpan.Zero),
            Latitude = 7.22d,
            Longitude = 126.97d,
            DepthKm = 23d,
            Magnitude = 7.4d,
            Scale = MagnitudeType.Mw,
            SourceUrl =
                "https://earthquake.phivolcs.dost.gov.ph/2025_Earthquake_Information/October/2025_1010_0143_B5F.html",
        },

        new()
        {
            // 31 October 2019, Tulunan, Cotabato. The values agree with USGS at 6.5 — the *scales* do
            // not: PHIVOLCS names Ms, USGS names Mww. A surface-wave magnitude and a moment magnitude
            // are different quantities, so the platform refuses to difference them even though the
            // printed numbers are identical.
            BulletinId = "2019_1031_0111",
            OccurredAt = new DateTimeOffset(2019, 10, 31, 1, 11, 18, TimeSpan.Zero),
            Latitude = 6.92d,
            Longitude = 125.06d,
            DepthKm = 8d,
            Magnitude = 6.5d,
            Scale = MagnitudeType.Ms,
            SourceUrl =
                "https://earthquake.phivolcs.dost.gov.ph/2019_Earthquake_Information/October/2019_1031_0111_B3F.html",
        },

        new()
        {
            // 29 October 2019, the larger shock two days earlier in the same Cotabato sequence.
            BulletinId = "2019_1029_0104",
            OccurredAt = new DateTimeOffset(2019, 10, 29, 1, 4, 43, TimeSpan.Zero),
            Latitude = 6.81d,
            Longitude = 125.03d,
            DepthKm = 7d,
            Magnitude = 6.6d,
            Scale = MagnitudeType.Ms,
            SourceUrl =
                "https://earthquake.phivolcs.dost.gov.ph/2019_Earthquake_Information/October/2019_1029_0104_B3F.html",
        },
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var source = await EnsureBulletinSourceAsync(now, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        var attached = 0;

        foreach (var bulletin in Bulletins)
        {
            if (await AttachAsync(bulletin, source, now, cancellationToken))
            {
                attached++;
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        BulletinSeedLog.Completed(logger, attached, Bulletins.Length);
    }

    private async Task<DataSource> EnsureBulletinSourceAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await context.DataSources
            .FirstOrDefaultAsync(source => source.Slug == BulletinSourceSlug, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var created = DataSource.Create(
                BulletinSourceSlug,
                agency: "DOST-PHIVOLCS",
                datasetName: "Earthquake Information bulletins (transcribed)",
                SourceAccessKind.ManualImport,
                attribution: "Earthquake parameters as published by DOST-PHIVOLCS in its Earthquake Information bulletins.",
                now)
            .Value
            .WithLinks(
                sourceUrl: "https://earthquake.phivolcs.dost.gov.ph/",
                termsUrl: "https://www.phivolcs.dost.gov.ph/")
            .WithCoverage(
                minimumReliableMagnitude: null,
                coverageNotes:
                    "Figures transcribed by hand from individual PHIVOLCS earthquake bulletins for a "
                    + "small number of reference events. This is not the PHIVOLCS catalogue and makes no "
                    + "claim to completeness — it exists so that events of particular significance can "
                    + "show the Philippine authority's own reported parameters alongside the "
                    + "international catalogue. Each entry links to the published bulletin it came from. "
                    + "PHIVOLCS is the authoritative source for Philippine earthquake information.")
            .WithPermissions(isRedistributable: true, isAuthoritativeForPhilippines: true);

        context.DataSources.Add(created);

        return created;
    }

    /// <summary>
    /// Finds the event this bulletin describes and attaches the reading to it.
    /// </summary>
    /// <remarks>
    /// This is event matching in its simplest useful form: candidates are those whose
    /// origin time falls inside a short window, and the nearest of those within a
    /// radius wins. Agencies typically agree on origin time to within seconds and on
    /// location to within tens of kilometres, so a three-minute window and a 60 km
    /// radius are generous without being ambiguous at this magnitude.
    /// <para>
    /// Deliberately conservative: if nothing matches, the bulletin is skipped and
    /// logged rather than creating a duplicate event. A spurious second event for the
    /// 2017 earthquake would be worse than a missing comparison.
    /// </para>
    /// </remarks>
    private async Task<bool> AttachAsync(
        BulletinRecord bulletin,
        DataSource source,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var alreadyRecorded = await context.EarthquakeObservations.AnyAsync(
            observation => observation.DataSourceId == source.Id
                && observation.ExternalEventId == bulletin.BulletinId,
            cancellationToken);

        if (alreadyRecorded)
        {
            return false;
        }

        var epicentre = Wgs84.Point(bulletin.Latitude, bulletin.Longitude);
        var windowStart = bulletin.OccurredAt - bulletin.MatchWindow;
        var windowEnd = bulletin.OccurredAt + bulletin.MatchWindow;
        var radiusMetres = bulletin.MatchRadiusKm * 1_000d;

        var match = await context.HazardEvents
            .Include(candidate => candidate.Observations)
            .Where(candidate => candidate.CanonicalOccurredAt >= windowStart
                && candidate.CanonicalOccurredAt <= windowEnd
                && candidate.CanonicalEpicenter.Distance(epicentre) <= radiusMetres)
            .OrderBy(candidate => candidate.CanonicalEpicenter.Distance(epicentre))
            .FirstOrDefaultAsync(cancellationToken);

        if (match is null)
        {
            BulletinSeedLog.NoMatch(logger, bulletin.BulletinId, bulletin.OccurredAt);

            return false;
        }

        var result = match.AddObservation(
            source.Id,
            bulletin.BulletinId,
            bulletin.OccurredAt,
            epicentre,
            bulletin.DepthKm is null
                ? DepthReading.Unknown
                : DepthReading.Constrained(bulletin.DepthKm.Value),
            new MagnitudeReading(bulletin.Magnitude, bulletin.Scale),
            now,
            bulletin.SourceUrl);

        if (result.IsFailure)
        {
            BulletinSeedLog.AttachFailed(logger, bulletin.BulletinId, result.Error!.Code);

            return false;
        }

        // The Philippine authority's reading becomes the preferred one for display.
        // PHIVOLCS is authoritative for Philippine earthquakes, and showing its figure
        // first is the correct default for a platform about the Philippines. The USGS
        // reading remains fully visible beside it.
        match.SetPreferredObservation(result.Value.Id, now);

        BulletinSeedLog.Attached(logger, bulletin.BulletinId, match.Id);

        return true;
    }
}

internal static partial class BulletinSeedLog
{
    [LoggerMessage(
        EventId = 6100,
        Level = LogLevel.Information,
        Message = "Bulletin observations: {AttachedCount} of {TotalCount} attached")]
    public static partial void Completed(ILogger logger, int attachedCount, int totalCount);

    [LoggerMessage(
        EventId = 6101,
        Level = LogLevel.Information,
        Message = "Bulletin {BulletinId} attached to event {EventId}")]
    public static partial void Attached(ILogger logger, string bulletinId, Guid eventId);

    [LoggerMessage(
        EventId = 6102,
        Level = LogLevel.Warning,
        Message = "Bulletin {BulletinId} at {OccurredAt:u} matched no ingested event; skipped rather than "
            + "creating a duplicate. Run the backfill for that period first.")]
    public static partial void NoMatch(ILogger logger, string bulletinId, DateTimeOffset occurredAt);

    [LoggerMessage(
        EventId = 6103,
        Level = LogLevel.Warning,
        Message = "Bulletin {BulletinId} could not be attached: {ErrorCode}")]
    public static partial void AttachFailed(ILogger logger, string bulletinId, string errorCode);
}
