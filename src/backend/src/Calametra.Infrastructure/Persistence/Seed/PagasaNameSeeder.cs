using Calametra.Domain.Events;
using Calametra.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Calametra.Infrastructure.Persistence.Seed;

/// <summary>
/// Applies PAGASA local names to imported cyclones.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is curated rather than ingested.</b> PAGASA assigns a Philippine name to every
/// cyclone entering its Area of Responsibility from its own rotating lists, independently of the
/// international name. It publishes those lists as web pages and PDFs, not as data, and — more
/// importantly — it does not publish a crosswalk from international name to local name. Building
/// that mapping requires per-storm knowledge. The same situation as the earthquake bulletins,
/// and handled the same way: transcribed deliberately, attributed, and limited in scope rather
/// than guessed at scale.
/// </para>
/// <para>
/// <b>Matched on name AND season.</b> International names rotate roughly every five years, so
/// the archive contains MERANTI in 2010 and 2016, GONI in 2015 and 2020, MAWAR in 2012, 2017 and
/// 2023. Matching on name alone would attach Rolly — the 2020 Goni — to the 2015 storm of the
/// same name. Verified against the imported set before this list was written.
/// </para>
/// <para>
/// <b>⚠ These mappings are hand-transcribed and have not been machine-verified against PAGASA's
/// own published records.</b> They cover well-documented storms and are believed correct, but
/// they should be checked against the authority's records before the work is defended. The
/// coverage note on the data source says so in the interface as well as here.
/// </para>
/// </remarks>
public sealed class PagasaNameSeeder(
    ApplicationDbContext context,
    TimeProvider timeProvider,
    ILogger<PagasaNameSeeder> logger)
{
    /// <summary>
    /// International name, season, PAGASA name, and how firmly the mapping is held.
    /// </summary>
    /// <remarks>
    /// Restricted to storms with a significant Philippine impact, which is where the local name
    /// is the one in public memory. An absent mapping renders as no local name rather than as a
    /// guess, and the interface explains that the gap is this platform's rather than the storm's.
    /// <para>
    /// <see cref="CrosswalkEntry.Certain"/> distinguishes mappings that are widely and
    /// consistently documented from those recalled with less confidence. Both are seeded, because
    /// a plausible name shown with a caveat is more useful than no name at all — but the flag
    /// records which entries a reviewer should check first, and the log reports the split.
    /// </para>
    /// </remarks>
    private sealed record CrosswalkEntry(
        string International,
        int Season,
        string Local,
        bool Certain);

    private static readonly CrosswalkEntry[] Crosswalk =
    [
        // Retired international names, each following a catastrophic Philippine landfall. These
        // are the most heavily documented storms in the archive and the mappings are unambiguous.
        new("BOPHA", 2012, "Pablo", true),
        new("HAIYAN", 2013, "Yolanda", true),
        new("RAMMASUN", 2014, "Glenda", true),
        new("HAGUPIT", 2014, "Ruby", true),
        new("KOPPU", 2015, "Lando", true),
        new("MELOR", 2015, "Nona", true),
        new("MERANTI", 2016, "Ferdie", true),
        new("HAIMA", 2016, "Lawin", true),
        new("NOCK-TEN", 2016, "Nina", true),
        new("MANGKHUT", 2018, "Ompong", true),
        new("YUTU", 2018, "Rosita", true),
        new("KAMMURI", 2019, "Tisoy", true),
        new("PHANFONE", 2019, "Ursula", true),
        new("VONGFONG", 2020, "Ambo", true),
        new("GONI", 2020, "Rolly", true),
        new("VAMCO", 2020, "Ulysses", true),
        new("SURIGAE", 2021, "Bising", true),
        new("RAI", 2021, "Odette", true),
        new("NORU", 2022, "Karding", true),
        new("NALGAE", 2022, "Paeng", true),
        new("DOKSURI", 2023, "Egay", true),
        new("MAWAR", 2023, "Betty", true),

        // Pre-2010 storms, added once the IBTrACS import was extended back to 1945. Until then the
        // archive began at season 2010, so the names in longest public memory — Ondoy, Pepeng,
        // Reming, Milenyo, Uring, Nitang — resolved to nothing at all. Each was verified against a
        // published account before being written here, and the seasons were checked against the
        // imported set, which matters more than usual in this group: several of these international
        // names appear repeatedly in the archive.
        new("IKE", 1984, "Nitang", true),
        new("MIKE", 1990, "Ruping", true),
        new("THELMA", 1991, "Uring", true),
        new("ANGELA", 1995, "Rosing", true),
        new("XANGSANE", 2006, "Milenyo", true),
        new("DURIAN", 2006, "Reming", true),
        new("KETSANA", 2009, "Ondoy", true),
        new("PARMA", 2009, "Pepeng", true),
        new("WASHI", 2011, "Sendong", true),

        // The local name is reused too, which is easy to miss because the crosswalk is written
        // international-name-first. PAGASA assigned Reming to Xangsane in 2000 and again to Durian
        // in 2006, so "Reming" legitimately answers to two storms in this archive and the mapping is
        // not one-to-one in either direction.
        new("XANGSANE", 2000, "Reming", true),

        // Frank is widely used for the 2008 Fengshen but the mapping was not confirmed against a
        // published account in the same pass as the group above, so it is held with less certainty.
        new("FENGSHEN", 2008, "Frank", false),

        // Storms that reach the top of an intensity-ordered list and would otherwise appear
        // unnamed. Held with less certainty than the group above and flagged accordingly.
        new("MEGI", 2010, "Juan", true),
        new("MAYSAK", 2015, "Chedeng", false),
        new("SOUDELOR", 2015, "Hanna", false),
        new("NEPARTAK", 2016, "Butchoy", false),
        new("CHANTHU", 2021, "Kiko", false),
        new("YAGI", 2024, "Enteng", false),

        // Researched against sources on 2026-09-13, targeting the thirty strongest storms in the
        // archive that still resolved to no local name. Ordered by peak wind, so this group is the
        // storms a reader is most likely to look for and least likely to know by their international
        // name.
        //
        // Five carry `true` because a PAGASA-authored document pairs both names in its own text — a
        // tropical cyclone bulletin or a preliminary report on pubfiles.pagasa.dost.gov.ph, or the
        // agency's Typhoon Committee member report.
        new("HINNAMNOR", 2022, "Henry", true),
        new("SAOLA", 2023, "Goring", true),
        new("MAN-YI", 2024, "Pepito", true),
        new("RAGASA", 2025, "Nando", true),
        new("BAVI", 2026, "Inday", true),

        // The remainder are held with less certainty by the same rule as the group above: the
        // pairings come from seasonal summaries and retired-name lists that do not themselves cite
        // PAGASA. They are seeded because a sourced pairing shown with a caveat is more useful than
        // an unnamed storm, and the flag is what tells a reviewer where to start.
        //
        // Season was verified per storm rather than per name, which matters more here than anywhere
        // else in this table: NANMADOL is Mina in 2011 and Josie in 2022, NOUL is Dodong in 2015 and
        // Kiyapo in 2026, SAOLA is Goring in 2023 and Gener in 2012, and BAVI is Inday in 2026 and
        // Betty in 2015. A join on name alone would have written five wrong names.
        // Recorded as Warling, which is what the 1979 season list gives. PAGASA's own decommissioned-
        // name table spells the same replacement Waling, so the two conflict in the agency's own
        // records; the variant is noted here rather than silently normalised to one spelling.
        new("TIP", 1979, "Warling", false),
        new("VERA", 1979, "Yayang", false),
        new("RITA", 1978, "Kading", false),
        new("DOT", 1985, "Saling", false),
        new("PEGGY", 1986, "Gading", false),
        new("NINA", 1987, "Sisang", false),
        new("BETTY", 1987, "Herming", false),
        new("LYNN", 1987, "Pepang", false),
        new("NELSON", 1988, "Paring", false),
        new("GORDON", 1989, "Goring", false),
        new("ELSIE", 1989, "Tasing", false),
        new("PAGE", 1990, "Tering", false),
        new("RUTH", 1991, "Trining", false),
        new("WALT", 1991, "Karing", false),
        new("YVETTE", 1992, "Ningning", false),
        new("DOUG", 1994, "Ritang", false),
        new("HERB", 1996, "Huaning", false),
        new("IVAN", 1997, "Narsing", false),
        new("ZEB", 1998, "Iliang", false),
        new("NANMADOL", 2011, "Mina", false),
        new("SONGDA", 2011, "Chedeng", false),
        new("JELAWAT", 2012, "Lawin", false),
        new("NEOGURI", 2014, "Florita", false),
        new("NOUL", 2015, "Dodong", false),
        new("KONG-REY", 2024, "Leon", false),
    ];

    /// <summary>Slug of the curated local-name crosswalk.</summary>
    /// <remarks>
    /// Registered by <c>ReferenceDataSeeder</c>, and it was missing entirely until the credits
    /// page was generated from the source table. Twenty-eight names were being shown in the
    /// interface with no row anywhere recording where they came from or how firmly they are held
    /// — which is the failure this platform exists to prevent, made by the platform itself.
    /// <para>
    /// The name is written onto <c>HazardEvent.LocalName</c>, which carries no source column of
    /// its own: a name is not a measurement and the aggregate deliberately holds no per-field
    /// provenance. That row is therefore the attribution, and its coverage note is where the
    /// hand-transcription warning reaches a reader rather than only this file.
    /// </para>
    /// </remarks>
    public const string CrosswalkSourceSlug = "pagasa-cyclone-names";

    /// <summary>How many storms the crosswalk maps. Read by the seeder that writes the note.</summary>
    /// <remarks>
    /// Exposed so the figure on the credits page is derived from the list itself. A number typed
    /// into a coverage note falls out of step with the list the first time an entry is added.
    /// </remarks>
    public static int CrosswalkCount => Crosswalk.Length;

    /// <summary>How many entries are held with lower confidence.</summary>
    public static int LessCertainCount => Array.FindAll(Crosswalk, entry => !entry.Certain).Length;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();

        // No value without its source, applied to this platform's own curated content: the names
        // are PAGASA's and the row recording that has to exist before any of them is written.
        // Reference data is seeded before ingestion in every host, so this is a guard rather than
        // an expected path.
        var registered = await context.DataSources
            .AnyAsync(source => source.Slug == CrosswalkSourceSlug, cancellationToken);

        if (!registered)
        {
            PagasaNameLog.SourceNotRegistered(logger, CrosswalkSourceSlug);

            return;
        }

        var names = Crosswalk.Select(entry => entry.International).Distinct().ToList();

        // Loaded tracked, because the local name is written back. Filtered by name first so the
        // query returns tens of rows rather than every cyclone held.
        var storms = await context.HazardEvents
            .Where(hazardEvent => hazardEvent.Type == HazardEventType.TropicalCyclone
                && hazardEvent.Name != null
                && names.Contains(hazardEvent.Name))
            .ToListAsync(cancellationToken);

        var applied = 0;
        var appliedUncertain = 0;

        foreach (var entry in Crosswalk)
        {
            // Both name and season, for the reason in the remarks above.
            var match = storms.Find(storm =>
                string.Equals(storm.Name, entry.International, StringComparison.OrdinalIgnoreCase)
                && storm.CanonicalOccurredAt.Year == entry.Season);

            if (match is null)
            {
                // Expected for seasons outside the imported range. Not an error.
                continue;
            }

            if (match.LocalName is not null)
            {
                continue;
            }

            match.AssignLocalName(entry.Local, now);
            applied++;

            if (!entry.Certain)
            {
                appliedUncertain++;
            }
        }

        if (applied > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        PagasaNameLog.Completed(logger, applied, Crosswalk.Length, appliedUncertain);
    }
}

internal static partial class PagasaNameLog
{
    [LoggerMessage(
        EventId = 9101,
        Level = LogLevel.Warning,
        Message = "PAGASA local names were not applied: source {Slug} is not registered, and a "
            + "name cannot be stored without the row recording where it came from. Seed reference "
            + "data first.")]
    public static partial void SourceNotRegistered(ILogger logger, string slug);

    [LoggerMessage(
        EventId = 9100,
        Level = LogLevel.Information,
        Message = "PAGASA local names applied to {AppliedCount} of {CrosswalkCount} known storms; "
            + "{UncertainCount} are held with lower confidence and should be checked against "
            + "PAGASA's published records")]
    public static partial void Completed(
        ILogger logger,
        int appliedCount,
        int crosswalkCount,
        int uncertainCount);
}
