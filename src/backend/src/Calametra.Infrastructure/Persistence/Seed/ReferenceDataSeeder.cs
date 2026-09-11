using Calametra.Domain.Hazards;
using Calametra.Domain.Sources;
using Calametra.Infrastructure.Sources.Gem;
using Calametra.Infrastructure.Sources.GeoNames;
using Calametra.Infrastructure.Sources.Ibtracs;
using Calametra.Infrastructure.Sources.Phivolcs;
using Calametra.Infrastructure.Sources.Usgs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Calametra.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds the data sources and hazard layer catalogue.
/// </summary>
/// <remarks>
/// This is reference data, not sample data: the platform cannot ingest or display
/// anything until it exists, because every observation requires a source and every
/// layer requires a catalogue entry. Seeding is idempotent and keyed on slug, so it
/// runs safely on every start.
/// <para>
/// The coverage notes and permission flags here are the load-bearing part. They are
/// what the About Data page renders and what the UI uses to caption event counts, so
/// getting them wrong would make the platform misrepresent its own limitations.
/// </para>
/// </remarks>
public sealed class ReferenceDataSeeder(
    ApplicationDbContext context,
    TimeProvider timeProvider,
    ILogger<ReferenceDataSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();

        var usgs = await EnsureUsgsAsync(now, cancellationToken);
        var gem = await EnsureGemAsync(now, cancellationToken);
        var activeFault = await EnsurePhivolcsActiveFaultAsync(now, cancellationToken);
        var trenches = await EnsurePhivolcsTrenchesAsync(now, cancellationToken);

        await EnsureCycloneAgenciesAsync(now, cancellationToken);
        await EnsureGeoNamesAsync(now, cancellationToken);
        await EnsurePagasaNamesAsync(now, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        await EnsureFaultLayerAsync(activeFault, now, cancellationToken);
        await EnsureTrenchLayerAsync(trenches, now, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        SeedLog.Completed(logger, usgs.Slug);
        _ = gem;
    }

    /// <summary>
    /// The cyclone agencies, plus the archive that aggregates them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Six sources, not one. IBTrACS is a compilation, and the wind speeds inside it belong to
    /// the agencies that computed them — so each is registered separately and every track point
    /// is attributed to the agency whose analysis it is. Registering only "IBTrACS" would make
    /// four disagreeing wind speeds look like one archive contradicting itself.
    /// </para>
    /// <para>
    /// Each coverage note states the agency's averaging period, because that is the fact which
    /// determines whether its readings may be compared with another's. JTWC's one-minute mean
    /// and JMA's ten-minute mean are different quantities, and the platform refuses to
    /// difference them.
    /// </para>
    /// </remarks>
    private async Task EnsureCycloneAgenciesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var definitions = new (string Slug, string Agency, string Dataset, string Averaging, bool Authoritative)[]
        {
            (IbtracsOptions.ArchiveSlug, "NOAA National Centers for Environmental Information",
                "IBTrACS v04r01 — International Best Track Archive for Climate Stewardship",
                "Compilation only; wind values belong to the contributing agencies.", false),

            ("jtwc-best-track", "Joint Typhoon Warning Center",
                "JTWC best track, via IBTrACS",
                "One-minute sustained wind. Systematically not comparable with the ten-minute "
                + "means published by JMA, Hong Kong and Korea.", false),

            ("jma-best-track", "Japan Meteorological Agency",
                "JMA best track, via IBTrACS",
                "Ten-minute sustained wind, the WMO standard. JMA is the designated regional "
                + "specialised centre for the western North Pacific, so its values are also what "
                + "IBTrACS reports in its WMO columns for this basin.", false),

            ("cma-best-track", "China Meteorological Administration",
                "CMA best track, via IBTrACS",
                "Two-minute sustained wind. Comparable with neither the one-minute nor the "
                + "ten-minute conventions.", false),

            ("hko-best-track", "Hong Kong Observatory",
                "HKO best track, via IBTrACS",
                "Ten-minute sustained wind.", false),

            ("kma-best-track", "Korea Meteorological Administration",
                "KMA best track, via IBTrACS",
                "Ten-minute sustained wind.", false),
        };

        foreach (var definition in definitions)
        {
            var existing = await context.DataSources
                .FirstOrDefaultAsync(source => source.Slug == definition.Slug, cancellationToken);

            var source = existing ?? DataSource.Create(
                    definition.Slug,
                    definition.Agency,
                    definition.Dataset,
                    SourceAccessKind.BulkFile,
                    attribution:
                        "Tropical cyclone best-track data from NOAA IBTrACS "
                        + "(Knapp et al., 2010), compiled from the contributing agencies.",
                    now)
                .Value;

            // Reconciled on every run rather than only at creation, matching the USGS entry:
            // these notes are the platform's own description of a source's limits and should
            // not be frozen at whatever was true when the database was first seeded.
            source
                .WithLinks(
                    sourceUrl: "https://www.ncei.noaa.gov/products/international-best-track-archive",
                    termsUrl: "https://www.ncei.noaa.gov/access/archive-access")
                .WithCoverage(
                    minimumReliableMagnitude: null,
                    coverageNotes:
                        definition.Averaging
                        + "\n\nBest-track data is a post-season reanalysis, not the warnings issued "
                        + "at the time. It is the best retrospective estimate of where a storm went "
                        + "and how strong it was, and it differs from what was forecast or warned "
                        + "during the event.\n\n"
                        + "Covers seasons from 1945, and intensity is NOT homogeneous across that "
                        + "span. JTWC documents its western North Pacific best-track corrections "
                        + "only for 1950-2000, rates 1985-2000 as high quality, and urges caution "
                        + "with older records; published assessments describe wind reports in older "
                        + "best tracks as likely of low quality. Position is more dependable than "
                        + "intensity in the early record. Treat a pre-satellite peak wind as a "
                        + "weaker claim than a modern one, and do not read a trend across the whole "
                        + "span as a change in the weather.\n\n"
                        + "Storm names here are the international ones. Philippine storms also carry "
                        + "a PAGASA local name — Haiyan was Yolanda. This archive holds a curated "
                        + "crosswalk of 39 of them, so a missing local name is a gap here rather "
                        + "than a storm that had none.")
                .WithPermissions(isRedistributable: true, isAuthoritativeForPhilippines: definition.Authoritative);

            if (existing is null)
            {
                context.DataSources.Add(source);
            }
        }
    }

    /// <summary>
    /// GEM Global Active Faults — the openly licensed fault source.
    /// </summary>
    /// <remarks>
    /// Registered as a first-class source rather than a stand-in. It is the only
    /// fault dataset Calametra may currently store, so it is what makes spatial
    /// analysis against faults possible at all; PHIVOLCS remains the authoritative
    /// national reference for display. Keeping both as separate sources lets the
    /// interface show them side by side and lets a user see that they differ in
    /// resolution, rather than presenting one silently as the other.
    /// </remarks>
    private async Task<DataSource> EnsureGemAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await context.DataSources
            .FirstOrDefaultAsync(source => source.Slug == GemOptions.Slug, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var created = DataSource.Create(
                GemOptions.Slug,
                agency: "GEM Foundation",
                datasetName: "Global Active Faults Database (harmonised)",
                SourceAccessKind.BulkFile,
                attribution:
                    "Active fault traces from the GEM Global Active Faults Database, "
                    + "licensed CC BY-SA 4.0.",
                now)
            .Value
            .WithLinks(
                sourceUrl: "https://github.com/GEMScienceTools/gem-global-active-faults",
                termsUrl: "https://creativecommons.org/licenses/by-sa/4.0/")
            .WithCoverage(
                minimumReliableMagnitude: null,
                coverageNotes:
                    "Global research compilation of active fault traces. The Philippine catalogue "
                    + "contributes 116 named faults, plus subduction thrust segments from the "
                    + "surrounding plate-boundary catalogues. This is a regional-scale compilation "
                    + "showing principal named faults: DOST-PHIVOLCS publishes far more detailed "
                    + "traces for the same areas, and this dataset is not a substitute for national "
                    + "hazard mapping. Slip type, slip rate and dip are recorded where the "
                    + "compilation provides them.")
            // Redistributable, but share-alike: any derived fault dataset Calametra
            // publishes carries CC BY-SA 4.0, and attribution is mandatory.
            .WithPermissions(isRedistributable: true, isAuthoritativeForPhilippines: false);

        context.DataSources.Add(created);

        return created;
    }

    /// <summary>
    /// GeoNames — the gazetteer behind the place directory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registered as its own source rather than folded into the platform's own furniture, because
    /// a place name, its administrative level and its coordinate are somebody's published claim
    /// and this platform attributes claims. It is also the source a reader should be pointed at
    /// when a place is missing or a code is absent.
    /// </para>
    /// <para>
    /// Not authoritative for the Philippines. The Philippine Statistics Authority is the authority
    /// for the Philippine Standard Geographic Code, and the coverage note below states exactly
    /// where this dataset and the PSA's own register diverge.
    /// </para>
    /// </remarks>
    private async Task<DataSource> EnsureGeoNamesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await context.DataSources
            .FirstOrDefaultAsync(source => source.Slug == GeoNamesOptions.Slug, cancellationToken);

        var created = existing ?? DataSource.Create(
                GeoNamesOptions.Slug,
                agency: "GeoNames",
                datasetName: "GeoNames geographical database — Philippine administrative divisions",
                SourceAccessKind.BulkFile,
                attribution: "Place names and coordinates from GeoNames, licensed CC BY 4.0.",
                now)
            .Value;

        // Reconciled on every run, like the USGS entry: these notes record measurements of the
        // upstream file, and a create-only seeder would leave an existing database describing
        // itself inaccurately after the next re-measurement.
        created
            .WithLinks(
                sourceUrl: "https://download.geonames.org/export/dump/",
                termsUrl: "https://creativecommons.org/licenses/by/4.0/")
            .WithCoverage(
                minimumReliableMagnitude: null,
                coverageNotes:
                    "Administrative places for the Philippines: 17 regions, 87 province-level units "
                    + "and 1,646 cities and municipalities. Barangays are present upstream but are "
                    + "not imported. Three limits matter when reading this directory.\n\n"
                    + "The codes are the pre-2019 nine-digit Philippine Standard Geographic Code, "
                    + "not the ten-digit edition the PSA publishes today, and no crosswalk between "
                    + "the two is offered here. The recoding cannot be computed: for most provincial "
                    + "municipalities the digits can be re-sliced — Adams is 012801000 here and "
                    + "0102801000 in the current register — but Metro Manila was recoded wholesale, "
                    + "where Quezon City is 137404000 here against 1381300000 today. A derived code "
                    + "would therefore be right in most places and confidently wrong in the capital, "
                    + "so none is derived.\n\n"
                    + "City and municipality are not distinguished upstream; both are third-order "
                    + "administrative divisions, which is what the PSGC itself calls the level. The "
                    + "distinction shown here is read from the official name. Checked against the "
                    + "PSA register: every one of its 148 cities carries a name marker and none of "
                    + "its 1,486 municipalities does, so a municipality is never labelled a city, "
                    + "but around ten cities whose names are abbreviated here appear as "
                    + "municipalities.\n\n"
                    + "No boundaries and no population. Every place is held as a representative "
                    + "point, so a radius search is measured from that point and not from the "
                    + "built-up area or the administrative boundary — for a large municipality the "
                    + "difference is tens of kilometres. The upstream file does carry population "
                    + "figures, and they are deliberately not imported: GeoNames publishes no census "
                    + "year per record, and a population figure without its vintage is not a "
                    + "measurement this platform will store.")
            .WithPermissions(isRedistributable: true, isAuthoritativeForPhilippines: false);

        if (existing is null)
        {
            context.DataSources.Add(created);
        }

        return created;
    }

    /// <summary>
    /// DOST-PAGASA — the source of the Philippine cyclone names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This row was missing until the credits page began rendering the source table, and its
    /// absence was the platform breaking its own rule: 28 local names were being shown in the
    /// interface with nothing recording whose names they are or how firmly the mapping is held.
    /// <c>PagasaNameSeeder</c> now refuses to apply a name unless this row exists.
    /// </para>
    /// <para>
    /// Registered here rather than by that seeder, because a source is reference data and runs on
    /// every start, whereas applying names is part of the cyclone import. A fresh database
    /// therefore credits PAGASA before any storm has been ingested.
    /// </para>
    /// <para>
    /// The counts in the note are read from the crosswalk itself. A figure typed in here would
    /// fall out of step with the list the first time an entry was added.
    /// </para>
    /// </remarks>
    private async Task<DataSource> EnsurePagasaNamesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await context.DataSources.FirstOrDefaultAsync(
            source => source.Slug == PagasaNameSeeder.CrosswalkSourceSlug,
            cancellationToken);

        var created = existing ?? DataSource.Create(
                PagasaNameSeeder.CrosswalkSourceSlug,
                agency: "DOST-PAGASA",
                datasetName: "Philippine tropical cyclone names (curated crosswalk)",
                // Neither an API nor a bulk file: the authority publishes its rotating name lists
                // as web pages and PDFs, so this is a hand-made record of a published document —
                // the same access kind the transcribed earthquake bulletins carry.
                SourceAccessKind.ManualImport,
                attribution:
                    "Philippine tropical cyclone names assigned by DOST-PAGASA. Crosswalk from "
                    + "international name transcribed for this platform.",
                now)
            .Value;

        created
            .WithLinks(
                sourceUrl: "https://www.pagasa.dost.gov.ph/climate/tropical-cyclone-information",
                termsUrl: "https://www.pagasa.dost.gov.ph/")
            .WithCoverage(
                minimumReliableMagnitude: null,
                coverageNotes:
                    "Every cyclone entering the Philippine Area of Responsibility receives a PAGASA "
                    + "name, independently of its international name — Haiyan was Yolanda here. A "
                    + "missing local name in this platform is therefore a gap in this crosswalk, "
                    + "not a storm that had none.\n\n"
                    + $"{PagasaNameSeeder.CrosswalkCount} storms are mapped, of which "
                    + $"{PagasaNameSeeder.LessCertainCount} are held with lower confidence. PAGASA "
                    + "publishes its name lists as documents and publishes no crosswalk from "
                    + "international name to local name, so this mapping is transcribed by hand. It "
                    + "has NOT been machine-verified against the authority's own records and should "
                    + "be checked against them before being relied on.\n\n"
                    + "Mapped on name and season together, because international names are reused: "
                    + "this archive holds MERANTI in 2010 and 2016, GONI in 2015 and 2020, and "
                    + "MAWAR in 2012, 2017 and 2023. Local names are reused too — Reming was "
                    + "assigned to Xangsane in 2000 and to Durian in 2006 — so the mapping is "
                    + "one-to-one in neither direction.")
            // The names are PAGASA's; this transcribed crosswalk of them is the platform's own
            // record and may be served onward with the attribution above.
            .WithPermissions(isRedistributable: true, isAuthoritativeForPhilippines: true);

        if (existing is null)
        {
            context.DataSources.Add(created);
        }

        return created;
    }

    private async Task<DataSource> EnsureUsgsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await context.DataSources
            .FirstOrDefaultAsync(source => source.Slug == UsgsOptions.Slug, cancellationToken);

        var created = existing ?? DataSource.Create(
                UsgsOptions.Slug,
                agency: "United States Geological Survey",
                datasetName: "ComCat earthquake catalogue (FDSN event service)",
                SourceAccessKind.RestApi,
                attribution: "Earthquake data courtesy of the U.S. Geological Survey.",
                now)
            .Value;

        // Coverage notes are reconciled on every run, not only at creation. They are
        // derived from measurements of the live catalogue, and those measurements
        // change as the archive is extended — the completeness caveat below only
        // became necessary once the backfill reached 1900. A create-only seeder would
        // leave an existing database describing itself inaccurately.
        created
            .WithLinks(
                sourceUrl: "https://earthquake.usgs.gov/earthquakes/search/",
                termsUrl: "https://www.usgs.gov/information-policies-and-instructions/copyrights-and-credits")
            .WithCoverage(
                // Measured, not assumed. For the Philippine bounding box between
                // 2015-01-01 and 2026-09-01 the catalogue contains 8,722 events at
                // M0+, 8,722 at M3.5+ and 8,715 at M4.0+ — identical totals at the
                // lower thresholds, which means it holds effectively nothing below
                // M4.0 anywhere in the country, not merely in one region.
                minimumReliableMagnitude: 4.0d,
                coverageNotes:
                    "Global earthquake catalogue, covering the Philippines from 1900 to the present. "
                    + "Three limits matter when reading it.\n\n"
                    + "Small earthquakes are missing throughout. The catalogue contains effectively "
                    + "no events below magnitude 4.0 anywhere in the archipelago. The PHIVOLCS local "
                    + "seismic network records magnitude 2-3 events routinely, so counts shown here "
                    + "are substantially lower than the number of earthquakes that actually "
                    + "occurred.\n\n"
                    + "Completeness changes over time, and this is the most important caveat. "
                    + "Detection of magnitude 4-5 events depends on how many seismometers were "
                    + "operating, so the record thickens dramatically as instrumentation improved: "
                    + "365 events at M4.0+ for the whole of 1900-1950, against 8,717 for 2015-2026. "
                    + "That is a change in observation, NOT a change in seismicity. Large "
                    + "earthquakes were always detected — the M6.0+ rate is roughly steady at five "
                    + "to seven per year across the entire century — so only magnitude 6 and above "
                    + "should be compared between eras.\n\n"
                    + "Many depths were never measured. Roughly a third of events report a fixed "
                    + "default depth assigned by the agency because it could not be resolved: "
                    + "10 km or 35 km in the modern record, and 33 km or 15 km in the historical "
                    + "record. Depths range from 1 km to 667 km, the deepest reflecting slab "
                    + "seismicity beneath the archipelago.")
            .WithPermissions(isRedistributable: true, isAuthoritativeForPhilippines: false);

        // Only insert when it is genuinely new; an existing row is already tracked and
        // its reconciled values will be saved as an update.
        if (existing is null)
        {
            context.DataSources.Add(created);
        }

        return created;
    }

    private async Task<DataSource> EnsurePhivolcsActiveFaultAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await context.DataSources
            .FirstOrDefaultAsync(source => source.Slug == PhivolcsOptions.ActiveFaultSlug, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var created = DataSource.Create(
                PhivolcsOptions.ActiveFaultSlug,
                agency: "DOST-PHIVOLCS",
                datasetName: "Active Faults of the Philippines",
                // Rendered imagery only. The service's bulk vector query operation is
                // disabled, and reachability is not a redistribution licence.
                SourceAccessKind.WmsProxy,
                attribution: "Active fault data © DOST-PHIVOLCS. Displayed via the official public map service.",
                now)
            .Value
            .WithLinks(
                sourceUrl: "https://gisweb.phivolcs.dost.gov.ph/arcgis/rest/services/PHIVOLCSPublic/ActiveFault/MapServer",
                termsUrl: "https://www.phivolcs.dost.gov.ph/")
            .WithCoverage(
                minimumReliableMagnitude: null,
                coverageNotes:
                    "Nationwide active fault traces compiled by PHIVOLCS from geomorphic analysis of "
                    + "aerial photographs and satellite imagery, geologic, bathymetric and topographic "
                    + "maps, published literature and field survey. Displayed by proxying the agency's "
                    + "own rendered imagery; Calametra stores no fault geometry. Mapping scale varies "
                    + "by segment, so traces are indicative of location rather than survey-accurate. "
                    + "A fault appearing near an earthquake does not mean that fault caused it.")
            // False until PHIVOLCS grants written permission. Enforced: the domain
            // refuses to promote a layer to local vector storage while this is false.
            .WithPermissions(isRedistributable: false, isAuthoritativeForPhilippines: true);

        context.DataSources.Add(created);

        return created;
    }

    private async Task<DataSource> EnsurePhivolcsTrenchesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await context.DataSources
            .FirstOrDefaultAsync(source => source.Slug == PhivolcsOptions.TrenchesSlug, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var created = DataSource.Create(
                PhivolcsOptions.TrenchesSlug,
                agency: "DOST-PHIVOLCS",
                datasetName: "Trenches",
                SourceAccessKind.WmsProxy,
                attribution: "Trench data © DOST-PHIVOLCS. Displayed via the official public map service.",
                now)
            .Value
            .WithLinks(
                sourceUrl: "https://gisweb.phivolcs.dost.gov.ph/arcgis/rest/services/PHIVOLCSPublic/Trenches/MapServer",
                termsUrl: "https://www.phivolcs.dost.gov.ph/")
            .WithCoverage(
                minimumReliableMagnitude: null,
                coverageNotes:
                    "Subduction trench axes. Relevant nationally because the Philippine Trench lies "
                    + "immediately offshore to the east and produces the deep seismicity visible in "
                    + "the depth cross-section.")
            .WithPermissions(isRedistributable: false, isAuthoritativeForPhilippines: true);

        context.DataSources.Add(created);

        return created;
    }

    private async Task EnsureFaultLayerAsync(
        DataSource source,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Scoped by data source, not by hazard type alone. Two active-fault layers
        // now exist — the PHIVOLCS proxy and the GEM local vector set — so matching
        // on type alone would find the wrong one and skip this layer entirely.
        var existing = await context.HazardLayers
            .FirstOrDefaultAsync(
                layer => layer.DataSourceId == source.Id && layer.HazardType == HazardType.ActiveFault,
                cancellationToken);

        const string identifyEndpoint =
            "https://gisweb.phivolcs.dost.gov.ph/arcgis/rest/services/PHIVOLCSPublic/ActiveFault/MapServer/identify";

        if (existing is not null)
        {
            // Reconcile rather than skip. Seeding runs on every start, and reference
            // data does change — this endpoint was added after the first rows were
            // written. A create-only seeder means corrections never reach an existing
            // database, which is how seeded data silently drifts out of date.
            if (string.IsNullOrWhiteSpace(existing.FeatureInfoEndpoint))
            {
                existing.WithFeatureInfo(identifyEndpoint);
            }

            return;
        }

        var layer = HazardLayerDefinition.CreateRemote(
                source.Id,
                HazardType.ActiveFault,
                HazardLens.Seismic,
                displayName: "Active Faults",
                wmsEndpoint:
                    "https://gisweb.phivolcs.dost.gov.ph/arcgis/services/PHIVOLCSPublic/ActiveFault/MapServer/WMSServer",
                wmsLayerName: "0",
                now)
            .Value
            .WithExplainer(
                explainer:
                    "An active fault is a fracture in the Earth's crust that has moved in the recent "
                    + "geological past and is considered capable of moving again. PHIVOLCS maps these "
                    + "nationwide from field survey and remote sensing.",
                interpretationNote:
                    "A line on this map shows where a fault trace has been mapped. It does not "
                    + "indicate when the fault will move, and an earthquake occurring near a mapped "
                    + "fault does not establish that this fault produced it — only the responsible "
                    + "agency can determine that.")
            // Feature inspection uses the ArcGIS REST identify operation, not WMS
            // GetFeatureInfo. GetFeatureInfo on this service advertises GeoJSON and
            // returns an empty collection for every request; identify returns the
            // fault system, segment name and mapping year. Verified 2026-09-04.
            .WithFeatureInfo(identifyEndpoint)
            .WithPresentation(isEnabledByDefault: true, sortOrder: 10, supportsFeatureInfo: true);

        context.HazardLayers.Add(layer);
    }

    private async Task EnsureTrenchLayerAsync(
        DataSource source,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await context.HazardLayers
            .FirstOrDefaultAsync(
                layer => layer.DataSourceId == source.Id && layer.HazardType == HazardType.Trench,
                cancellationToken);

        const string identifyEndpoint =
            "https://gisweb.phivolcs.dost.gov.ph/arcgis/rest/services/PHIVOLCSPublic/Trenches/MapServer/identify";

        if (existing is not null)
        {
            if (string.IsNullOrWhiteSpace(existing.FeatureInfoEndpoint))
            {
                existing.WithFeatureInfo(identifyEndpoint);
            }

            return;
        }

        var layer = HazardLayerDefinition.CreateRemote(
                source.Id,
                HazardType.Trench,
                HazardLens.Seismic,
                displayName: "Trenches",
                wmsEndpoint:
                    "https://gisweb.phivolcs.dost.gov.ph/arcgis/services/PHIVOLCSPublic/Trenches/MapServer/WMSServer",
                wmsLayerName: "0",
                now)
            .Value
            .WithExplainer(
                explainer:
                    "A trench marks where one tectonic plate descends beneath another. The Philippines "
                    + "is ringed by them — the Manila Trench to the west, the Philippine Trench along "
                    + "the eastern seaboard, and the Negros and Cotabato Trenches to the south. They "
                    + "are the source of the deep earthquakes recorded beneath the archipelago, some "
                    + "reaching 667 km.",
                interpretationNote:
                    "Trench axes are regional features drawn at ocean-basin scale and are approximate "
                    + "at local zoom levels.")
            .WithFeatureInfo(identifyEndpoint)
            .WithPresentation(isEnabledByDefault: false, sortOrder: 20, supportsFeatureInfo: true);

        context.HazardLayers.Add(layer);
    }
}

internal static partial class SeedLog
{
    [LoggerMessage(
        EventId = 6000,
        Level = LogLevel.Information,
        Message = "Reference data seeded (verified {PrimarySourceSlug})")]
    public static partial void Completed(ILogger logger, string primarySourceSlug);
}
