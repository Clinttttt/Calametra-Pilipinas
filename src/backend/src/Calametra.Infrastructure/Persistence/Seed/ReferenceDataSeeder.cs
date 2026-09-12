using Calametra.Domain.Hazards;
using Calametra.Domain.Sources;
using Calametra.Infrastructure.Sources.Gem;
using Calametra.Infrastructure.Sources.GeoNames;
using Calametra.Infrastructure.Sources.Ibtracs;
using Calametra.Infrastructure.Sources.Mgb;
using Calametra.Infrastructure.Sources.OpenStreetMap;
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
        var groundShaking = await EnsurePhivolcsGroundShakingAsync(now, cancellationToken);
        var liquefaction = await EnsurePhivolcsLiquefactionAsync(now, cancellationToken);
        var earthquakeLandslide = await EnsurePhivolcsEarthquakeLandslideAsync(now, cancellationToken);
        var mgb = await EnsureMgbAsync(now, cancellationToken);

        await EnsureCycloneAgenciesAsync(now, cancellationToken);
        await EnsureGeoNamesAsync(now, cancellationToken);
        await EnsureOpenStreetMapAsync(now, cancellationToken);
        await EnsurePagasaNamesAsync(now, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        await EnsureFaultLayerAsync(activeFault, now, cancellationToken);
        await EnsureTrenchLayerAsync(trenches, now, cancellationToken);
        await EnsurePhivolcsHazardLayersAsync(
            groundShaking,
            liquefaction,
            earthquakeLandslide,
            now,
            cancellationToken);
        await EnsureMgbLayersAsync(mgb, now, cancellationToken);

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

    /// <summary>
    /// DOST-MGB — rainfall-triggered susceptibility, displayed and never stored.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Probed on 2026-09-11 before being registered. Both services are live on
    /// <c>controlmap.mgb.gov.ph</c>, expose the <c>WMSServer</c> extension, render EPSG:3857
    /// <c>GetMap</c> requests as PNG, and answer <c>identify</c> with a single susceptibility
    /// rating per polygon.
    /// </para>
    /// <para>
    /// <b>Not redistributable, because no licence is published.</b> The services carry an empty
    /// <c>copyrightText</c> and the layers are not ArcGIS Online items, so there is nothing that
    /// grants storage. This is the PHIVOLCS position exactly: reachability is not permission, and
    /// the domain refuses to store a feature from a source flagged this way.
    /// </para>
    /// <para>
    /// Registered as one source rather than two. The rain-induced landslide and flood maps come
    /// from the same programme, the same publisher and the same licence position; splitting them
    /// would suggest a difference in provenance that does not exist. They are two layers under it.
    /// </para>
    /// </remarks>
    private async Task<DataSource> EnsureMgbAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await context.DataSources
            .FirstOrDefaultAsync(source => source.Slug == MgbOptions.RainInducedLandslideSlug, cancellationToken);

        var created = existing ?? DataSource.Create(
                MgbOptions.RainInducedLandslideSlug,
                agency: "DOST-MGB",
                datasetName: "Detailed geohazard susceptibility maps (rain-induced landslide and flood)",
                SourceAccessKind.WmsProxy,
                attribution:
                    "Geohazard susceptibility mapping © DOST-Mines and Geosciences Bureau. "
                    + "Displayed via the agency's own public map service.",
                now)
            .Value;

        created
            .WithLinks(
                sourceUrl: "https://controlmap.mgb.gov.ph/arcgis/rest/services/GeospatialDataInventory_Public",
                termsUrl: "https://mgb.gov.ph/")
            .WithCoverage(
                minimumReliableMagnitude: null,
                coverageNotes:
                    "Susceptibility is not a forecast. These maps state that an area has terrain, "
                    + "geology or drainage that makes it prone to failure or inundation given "
                    + "enough rain — not that an event will occur, and not when.\n\n"
                    + "The ratings are MGB's own. Landslide susceptibility is published as Very "
                    + "High, High, Moderate and Low, plus a separate class for debris flow paths "
                    + "and possible accumulation zones; flood susceptibility as Very High, High, "
                    + "Moderate and Low. Calametra does not reclassify or combine them.\n\n"
                    + "Rainfall-triggered, and therefore not specific to any one storm. A tropical "
                    + "cyclone is the most common trigger in this country, which is why these "
                    + "layers appear alongside storm tracks, but monsoon rain produces the same "
                    + "failures and these extents describe neither event.\n\n"
                    + "Distinct from the earthquake-induced landslide hazard PHIVOLCS publishes. "
                    + "That map describes slope failure caused by ground shaking; this one "
                    + "describes failure caused by rain. They are different phenomena from "
                    + "different agencies and must not be read as one.\n\n"
                    + "Calametra stores none of this geometry. MGB publishes no licence for it — "
                    + "the services carry no copyright statement — so it is displayed by proxying "
                    + "the agency's own rendering, and no figure on this platform is computed from "
                    + "it. MGB also publishes a landslide inventory of 10,783 dated polygons, "
                    + "which for the same reason cannot be held here.")
            .WithPermissions(isRedistributable: false, isAuthoritativeForPhilippines: true);

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

    /// <summary>
    /// OpenStreetMap — where the towns actually are.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registered as its own source, and read for exactly one field. The gazetteer behind the place
    /// directory remains the authority for names, provinces and codes; this supplies the coordinate,
    /// because measured on 2026-09-12 the gazetteer's own points are not good enough for the
    /// distances this platform states from them — 1,090 of 1,647 are rounded to the nearest
    /// arc-minute, five to a quarter of a degree, and the mean distance to the mapped town centre is
    /// 5.2 km.
    /// </para>
    /// <para>
    /// <b>ODbL 1.0 is share-alike.</b> Attribution to OpenStreetMap contributors is mandatory and a
    /// derived database inherits the licence — the GEM position exactly, and the reason this is a
    /// separate row rather than a footnote on the gazetteer's. The basemap the browser draws is also
    /// OpenStreetMap, and that is credited separately from the client, because nothing of it is
    /// stored.
    /// </para>
    /// </remarks>
    private async Task<DataSource> EnsureOpenStreetMapAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await context.DataSources
            .FirstOrDefaultAsync(source => source.Slug == OpenStreetMapOptions.Slug, cancellationToken);

        var created = existing ?? DataSource.Create(
                OpenStreetMapOptions.Slug,
                agency: "OpenStreetMap contributors",
                datasetName: "Philippine city and town centres, via the Overpass API",
                SourceAccessKind.RestApi,
                attribution:
                    "Town centre coordinates © OpenStreetMap contributors, available under the Open "
                    + "Database Licence (ODbL) 1.0.",
                now)
            .Value;

        created
            .WithLinks(
                sourceUrl: "https://www.openstreetmap.org/",
                termsUrl: "https://opendatacommons.org/licenses/odbl/1-0/")
            .WithCoverage(
                minimumReliableMagnitude: null,
                coverageNotes:
                    "Read to place each city and municipality at its town centre — the poblacion, "
                    + "where the built-up area and the municipal hall are — rather than at the "
                    + "administrative point published by the gazetteer. 1,695 mapped centres were "
                    + "returned nationally on 2026-09-12: 155 tagged city and 1,540 tagged town, "
                    + "against 1,647 cities and municipalities in this directory.\n\n"
                    + "A volunteer map, and treated as one. It is not an authority on what a place "
                    + "is called, which province contains it, or whether it is a city — city status "
                    + "is conferred by law and read here from the official name in the PSGC "
                    + "register. Nothing but the coordinate is taken from it.\n\n"
                    + "A place is matched to a centre only when the names agree after folding and "
                    + "one candidate is clearly the closest. Philippine municipality names repeat "
                    + "across provinces, so where two same-named candidates are comparably close "
                    + "the place keeps its original coordinate and is counted as ambiguous rather "
                    + "than assigned the nearer one.\n\n"
                    + "Still a point and not a boundary. A radius is measured from the town centre, "
                    + "which for a large municipality is not its edge.")
            // Storable, and share-alike: any derived database Calametra publishes carries ODbL 1.0
            // and the attribution above is mandatory.
            .WithPermissions(isRedistributable: true, isAuthoritativeForPhilippines: false);

        if (existing is null)
        {
            context.DataSources.Add(created);
        }

        return created;
    }

    /// <summary>
    /// DOST-PHIVOLCS deterministic ground shaking — displayed and never stored.
    /// </summary>
    /// <remarks>
    /// A separate source row from the fault and trench datasets, and from liquefaction, because
    /// each is a separately published dataset with its own mapping projects and vintage, and the
    /// Sources page lists what the platform reads dataset by dataset. This is the opposite choice
    /// from <see cref="EnsureMgbAsync"/>, where two layers share one source: those come from one
    /// programme under one licence, whereas these were mapped by different studies.
    /// </remarks>
    private async Task<DataSource> EnsurePhivolcsGroundShakingAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await context.DataSources
            .FirstOrDefaultAsync(source => source.Slug == PhivolcsOptions.GroundShakingSlug, cancellationToken);

        var created = existing ?? DataSource.Create(
                PhivolcsOptions.GroundShakingSlug,
                agency: "DOST-PHIVOLCS",
                datasetName: "Ground shaking hazard maps (deterministic)",
                SourceAccessKind.WmsProxy,
                attribution:
                    "Ground shaking hazard data © DOST-PHIVOLCS. Displayed via the official public "
                    + "map service.",
                now)
            .Value;

        // Reconciled on every run rather than only at creation, as the MGB and USGS entries are:
        // these notes record what was measured of the service, and a create-only seeder would
        // leave an existing database describing itself as it was first understood.
        created
            .WithLinks(
                sourceUrl:
                    "https://gisweb.phivolcs.dost.gov.ph/arcgis/rest/services/PHIVOLCSPublic/GroundShaking/MapServer",
                termsUrl: "https://www.phivolcs.dost.gov.ph/")
            .WithCoverage(
                minimumReliableMagnitude: null,
                coverageNotes:
                    "Expected shaking on the PHIVOLCS Earthquake Intensity Scale, published as "
                    + "areas of Intensity VI (very strong), VII (destructive) and VIII (very "
                    + "destructive to devastating). Records carry the scale they were mapped at — "
                    + "1:50,000 for the areas checked — and the year of mapping and publication "
                    + "where the agency recorded them.\n\n"
                    + "PHIVOLCS labels this dataset deterministic: it is the shaking expected from "
                    + "a specific modelled earthquake scenario, not the probability of shaking over "
                    + "any period, and it says nothing about when such an earthquake will occur.\n\n"
                    + "Displayed by proxying the agency's own rendered imagery. Calametra stores "
                    + "none of this geometry and computes no figure from it: bulk vector query is "
                    + "disabled on the service and release requires a signed Data User Agreement, "
                    + "which is pending.")
            // False until PHIVOLCS grants written permission, exactly as for the fault traces.
            .WithPermissions(isRedistributable: false, isAuthoritativeForPhilippines: true);

        if (existing is null)
        {
            context.DataSources.Add(created);
        }

        return created;
    }

    /// <summary>
    /// DOST-PHIVOLCS liquefaction susceptibility — displayed and never stored.
    /// </summary>
    private async Task<DataSource> EnsurePhivolcsLiquefactionAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await context.DataSources
            .FirstOrDefaultAsync(source => source.Slug == PhivolcsOptions.LiquefactionSlug, cancellationToken);

        var created = existing ?? DataSource.Create(
                PhivolcsOptions.LiquefactionSlug,
                agency: "DOST-PHIVOLCS",
                datasetName: "Liquefaction susceptibility maps",
                SourceAccessKind.WmsProxy,
                attribution:
                    "Liquefaction susceptibility data © DOST-PHIVOLCS. Displayed via the official "
                    + "public map service.",
                now)
            .Value;

        created
            .WithLinks(
                sourceUrl:
                    "https://gisweb.phivolcs.dost.gov.ph/arcgis/rest/services/PHIVOLCSPublic/Liquefaction/MapServer",
                termsUrl: "https://www.phivolcs.dost.gov.ph/")
            .WithCoverage(
                minimumReliableMagnitude: null,
                coverageNotes:
                    "Where saturated loose ground may lose strength during shaking. The published "
                    + "legend carries two vocabularies — High, Moderate and Low Potential alongside "
                    + "Highly, Moderately, Generally and Least Susceptible — because areas were "
                    + "mapped by different studies, and the agency names the project per area. "
                    + "Calametra shows whichever class PHIVOLCS recorded and does NOT merge the two "
                    + "into a single scale.\n\n"
                    + "A susceptibility class describes the ground's tendency to liquefy given "
                    + "sufficient shaking. It is not a forecast, and it does not indicate when "
                    + "shaking will occur.\n\n"
                    + "Displayed by proxying the agency's own rendered imagery. Calametra stores "
                    + "none of this geometry and computes no figure from it: bulk vector query is "
                    + "disabled on the service and release requires a signed Data User Agreement, "
                    + "which is pending.")
            .WithPermissions(isRedistributable: false, isAuthoritativeForPhilippines: true);

        if (existing is null)
        {
            context.DataSources.Add(created);
        }

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
            // Off by default. It is proxied imagery rendered on demand, so it arrives seconds after
            // the map does — switched on automatically that looks like a stall, and the reader never
            // asked for it. Every layer in the catalogue now starts off, and the panel is where a
            // reader turns one on.
            .WithPresentation(isEnabledByDefault: false, sortOrder: 10, supportsFeatureInfo: true);

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

    /// <summary>
    /// DOST-PHIVOLCS earthquake-induced landslide — displayed and never stored.
    /// </summary>
    /// <remarks>
    /// Held out of the catalogue when these layers were first registered, on measurement: the service
    /// took 81 s to render a 400 km extent and 32.8 s at 78 km, which is unusable per tile. Re-measured
    /// on 2026-09-12 at a 39 km extent — one tile at zoom 10 — it returns in 8.1 s, so it is admitted
    /// with a minimum zoom rather than left out. The tsunami inundation service, measured the same way,
    /// still takes 139-143 s for a 20 km tile over Manila Bay and remains uncatalogued.
    /// </remarks>
    private async Task<DataSource> EnsurePhivolcsEarthquakeLandslideAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await context.DataSources
            .FirstOrDefaultAsync(
                source => source.Slug == PhivolcsOptions.EarthquakeInducedLandslideSlug,
                cancellationToken);

        var created = existing ?? DataSource.Create(
                PhivolcsOptions.EarthquakeInducedLandslideSlug,
                agency: "DOST-PHIVOLCS",
                datasetName: "Earthquake-induced landslide susceptibility maps",
                SourceAccessKind.WmsProxy,
                attribution:
                    "Earthquake-induced landslide data © DOST-PHIVOLCS. Displayed via the official "
                    + "public map service.",
                now)
            .Value;

        created
            .WithLinks(
                sourceUrl:
                    "https://gisweb.phivolcs.dost.gov.ph/arcgis/rest/services/PHIVOLCSPublic/"
                    + "EarthquakeInducedLandslide/MapServer",
                termsUrl: "https://www.phivolcs.dost.gov.ph/")
            .WithCoverage(
                minimumReliableMagnitude: null,
                coverageNotes:
                    "Slopes expected to fail under earthquake shaking. A different hazard from the "
                    + "rain-induced landslide susceptibility published by DOST-MGB: the trigger is "
                    + "ground motion rather than rainfall, so the two maps identify different "
                    + "slopes and neither supersedes the other.\n\n"
                    + "Drawn only when zoomed in. PHIVOLCS renders every tile of this service on "
                    + "demand and publishes no tile cache, and render time scales with the polygons "
                    + "in view: measured 8.1 s for a 39 km tile, 32.8 s at 78 km and 81 s across "
                    + "400 km. Requesting it at national zoom would spend a minute of the agency's "
                    + "server time per tile, so the platform does not ask.\n\n"
                    + "Displayed by proxying the agency's own rendered imagery. Calametra stores "
                    + "none of this geometry and computes no figure from it: bulk vector query is "
                    + "disabled on the service and release requires a signed Data User Agreement, "
                    + "which is pending.")
            .WithPermissions(isRedistributable: false, isAuthoritativeForPhilippines: true);

        if (existing is null)
        {
            context.DataSources.Add(created);
        }

        return created;
    }

    /// <summary>
    /// The two PHIVOLCS earthquake-hazard layers that can be served interactively.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Re-probed on 2026-09-12 rather than taken from the September note recording twelve services.
    /// All four earthquake-hazard services are live, expose <c>WMSServer</c>, publish a legend and
    /// answer <c>identify</c>. None publishes a fused tile cache, so every tile is rendered on
    /// demand — and that is what decided which two are registered here.
    /// </para>
    /// <para>
    /// <b>Two are deliberately left out, on measurement.</b> Render time scales with the number of
    /// polygons intersecting the view, and for two of the four it is prohibitive:
    /// </para>
    /// <list type="table">
    ///   <item><description>Ground shaking — 2.9 s over a 400 km extent, 0.2-3.9 s elsewhere</description></item>
    ///   <item><description>Liquefaction — 1.6 s over the same extent, 0.95 s elsewhere</description></item>
    ///   <item><description>Earthquake-induced landslide — 81 s over 400 km, 7.3 s over 10 km</description></item>
    ///   <item><description>Tsunami inundation — <b>140 s</b> over 400 km, 6.1 s over 10 km</description></item>
    /// </list>
    /// <para>
    /// Ground shaking follows the same curve further along and is admitted rather than excused: at a
    /// 1300 km extent — one tile at the national zoom — it takes 19.1 s through this platform's own
    /// proxy against 2.2 s for liquefaction. It succeeds inside the attempt timeout, it is off by
    /// default, and a fetched tile is cached for seven days, so the cost is paid once per area. A
    /// per-layer minimum zoom would improve it as well as admitting the two held back below.
    /// </para>
    /// <para>
    /// The tsunami layer failed with HTTP 500 through this platform's own proxy after 91 s on the
    /// Luzon coast, where far more polygons intersect than in the Mindanao extent it was first
    /// measured in. A layer that fails on the most populated coastline in the country is not a
    /// layer, and raising the timeout further would only trade a visible failure for a minute-long
    /// hang while holding a connection open on an agency's server per tile. Both are catalogued only
    /// once there is a path that does not require a national-extent render — a minimum zoom per
    /// layer, or stored geometry if the Data User Agreement is granted. Recorded in
    /// <c>docs/ROADMAP.md</c> and asserted by <c>HazardCatalogueTests</c>, so re-adding either is a
    /// deliberate act rather than an oversight.
    /// </para>
    /// <para>
    /// Both registered layers belong to the <see cref="HazardLens.Seismic"/> lens, which the
    /// earthquake view already reaches — unlike the volcanic services on the same server, which
    /// have no hazard a reader can select and are therefore deliberately not catalogued.
    /// </para>
    /// <para>
    /// <b>Every explainer states the vocabulary the agency actually publishes</b>, read from each
    /// service's own legend rather than described generically.
    /// </para>
    /// </remarks>
    private async Task EnsurePhivolcsHazardLayersAsync(
        DataSource groundShaking,
        DataSource liquefaction,
        DataSource earthquakeLandslide,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var phivolcs = new PhivolcsOptions();

        var definitions = new (DataSource Source, HazardType Type, string Service,
            string DisplayName, int SortOrder, int? MinimumZoom, string Explainer, string Note)[]
        {
            (groundShaking, HazardType.GroundShaking, "GroundShaking", "Ground Shaking", 21, 6,
                "How strongly the ground is expected to shake, on the PHIVOLCS Earthquake "
                + "Intensity Scale. PHIVOLCS publishes this layer at Intensity VI (very strong), "
                + "VII (destructive) and VIII (very destructive to devastating), and its records "
                + "carry the mapping scale — 1:50,000 for the areas checked.",
                "PHIVOLCS labels this layer deterministic: it maps the shaking expected from a "
                + "specific modelled earthquake scenario, not the probability of shaking over any "
                + "period. It is not a forecast and says nothing about when such an earthquake will "
                + "occur."),

            (liquefaction, HazardType.Liquefaction, "Liquefaction", "Liquefaction", 22, null,
                "Where saturated loose ground may lose strength and behave as a liquid during "
                + "shaking, which can sink or tilt structures whose foundations were sound. "
                + "PHIVOLCS records a class per area and names the project that mapped it.",
                "A susceptibility class is not a forecast. The published legend also carries two "
                + "vocabularies — High, Moderate and Low Potential alongside Highly, Moderately, "
                + "Generally and Least Susceptible — because areas were mapped by different "
                + "studies. Calametra shows whichever class the agency recorded for an area and "
                + "does not merge the two into one scale."),

            (earthquakeLandslide, HazardType.EarthquakeInducedLandslide,
                "EarthquakeInducedLandslide", "Earthquake-induced Landslide", 23, 10,
                "Where shaking is expected to bring slopes down. A separate hazard from the "
                + "rain-induced landslide susceptibility DOST-MGB publishes: the trigger here is "
                + "ground motion rather than rainfall, and the two maps disagree about which slopes "
                + "matter because the mechanisms differ.",
                "A susceptibility class is not a forecast, and it does not say which earthquake "
                + "would trigger the slope. This layer draws only when zoomed in — PHIVOLCS renders "
                + "it on demand, and a national view of it takes over a minute of the agency's "
                + "server time per tile."),
        };

        foreach (var definition in definitions)
        {
            // Scoped by data source as well as hazard type, for the reason the fault layer
            // records: one hazard type may be published by more than one agency.
            var existing = await context.HazardLayers
                .FirstOrDefaultAsync(
                    layer => layer.DataSourceId == definition.Source.Id
                        && layer.HazardType == definition.Type,
                    cancellationToken);

            var identifyEndpoint =
                $"{phivolcs.RestServicesRoot}/{definition.Service}/MapServer/identify";

            if (existing is not null)
            {
                // Reconciled rather than skipped, as the fault and MGB layers are.
                if (string.IsNullOrWhiteSpace(existing.FeatureInfoEndpoint))
                {
                    existing.WithFeatureInfo(identifyEndpoint);
                }

                // The minimum zoom arrived after these rows were first written, and without this a
                // database seeded yesterday would keep asking a government server for a national
                // render of a layer measured at 19 s per tile.
                if (definition.MinimumZoom is not null && existing.MinimumZoom is null)
                {
                    existing.WithMinimumZoom(definition.MinimumZoom.Value);
                }

                continue;
            }

            var layer = HazardLayerDefinition.CreateRemote(
                    definition.Source.Id,
                    definition.Type,
                    HazardLens.Seismic,
                    displayName: definition.DisplayName,
                    wmsEndpoint: $"{phivolcs.ServicesRoot}/{definition.Service}/MapServer/WMSServer",
                    wmsLayerName: "0",
                    now)
                .Value
                .WithExplainer(definition.Explainer, definition.Note)
                .WithFeatureInfo(identifyEndpoint)
                // No cached tile endpoint: neither service publishes a fused cache, unlike MGB's
                // two. Deliberately absent rather than guessed — a wrong template 404s per tile.
                .WithPresentation(
                    isEnabledByDefault: false,
                    sortOrder: definition.SortOrder,
                    supportsFeatureInfo: true);

            if (definition.MinimumZoom is not null)
            {
                layer.WithMinimumZoom(definition.MinimumZoom.Value);
            }

            context.HazardLayers.Add(layer);
        }
    }

    /// <summary>
    /// The two DOST-MGB susceptibility layers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Filed under the cyclone lens, which is a judgement worth stating.</b> These maps are
    /// rainfall-triggered and not storm-specific, so no lens fits them perfectly. The alternatives
    /// were worse: <c>Terrain</c> and <c>Coastal</c> are reachable by no hazard the reader can
    /// select, so layers filed there would be catalogued and invisible, and inventing a hazard mode
    /// for them would put a third card on the chooser with no event archive behind it. A tropical
    /// cyclone is the dominant rainfall driver in this country, so the storm view is where a reader
    /// asking "where does this rain cause landslides and floods" already is. The interpretation
    /// note carries the correction that the maps describe neither a storm nor a forecast.
    /// </para>
    /// <para>
    /// Both default to off. They are dense national polygon fills, and switching them on
    /// automatically would bury the track a reader opened the storm view to see.
    /// </para>
    /// <para>
    /// Endpoints are composed from <see cref="MgbOptions"/> rather than written out, because the
    /// OGC and REST paths differ by one segment and getting that wrong yields a 404 rather than an
    /// informative error.
    /// </para>
    /// </remarks>
    private async Task EnsureMgbLayersAsync(
        DataSource source,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var mgb = new MgbOptions();

        var definitions = new (HazardType Type, string Service, string DisplayName, int SortOrder,
            string Explainer, string Note)[]
        {
            (HazardType.RainInducedLandslide,
                mgb.RainInducedLandslideService,
                "Rain-induced Landslide Susceptibility",
                30,
                "Where terrain, geology and slope make ground failure likely when it rains hard "
                + "enough. MGB maps this nationwide at detailed scale and rates each area Very "
                + "High, High, Moderate or Low, with a separate class for debris flow paths and "
                + "the places debris comes to rest.",
                "Susceptibility is not a forecast. A Very High rating means the ground is prone to "
                + "failure given sufficient rain, not that it will fail, and not when. This is "
                + "also a different hazard from the earthquake-induced landslide map PHIVOLCS "
                + "publishes: that one describes slope failure caused by shaking."),

            (HazardType.Flood,
                mgb.FloodService,
                "Flood Susceptibility",
                31,
                "Where water is likely to collect or a channel to overtop when rainfall is heavy, "
                + "rated Very High, High, Moderate or Low. Derived from terrain and drainage "
                + "rather than from any particular storm.",
                "Susceptibility is not a forecast, and this is not a flood map of any event. It "
                + "describes the ground's tendency to flood given enough rain. Actual flooding "
                + "depends on how much rain falls, over how long, and on drainage that may have "
                + "changed since the mapping."),
        };

        foreach (var definition in definitions)
        {
            var existing = await context.HazardLayers
                .FirstOrDefaultAsync(
                    layer => layer.DataSourceId == source.Id && layer.HazardType == definition.Type,
                    cancellationToken);

            var identifyEndpoint =
                $"{mgb.RestServicesRoot}/{definition.Service}/MapServer/identify";

            // ArcGIS addresses a cached tile as level/row/column, so y precedes x. Measured
            // 2026-09-11: 60-120 ms per tile from this cache against 18.8-19.4 s for the same tile
            // rendered through WMS.
            var cachedTileEndpoint =
                $"{mgb.RestServicesRoot}/{definition.Service}/MapServer/tile/{{z}}/{{y}}/{{x}}";

            if (existing is not null)
            {
                // Reconciled rather than skipped, for the reason the fault layer records: seeding
                // runs on every start and a create-only seeder means a correction never reaches a
                // database that already has the row. The cached tile endpoint was added after these
                // two layers were first seeded, and without this they would have kept rendering
                // every tile on demand — 19 seconds each — while the code believed otherwise.
                if (string.IsNullOrWhiteSpace(existing.FeatureInfoEndpoint))
                {
                    existing.WithFeatureInfo(identifyEndpoint);
                }

                if (string.IsNullOrWhiteSpace(existing.CachedTileEndpoint))
                {
                    existing.WithCachedTiles(cachedTileEndpoint);
                }

                continue;
            }

            var layer = HazardLayerDefinition.CreateRemote(
                    source.Id,
                    definition.Type,
                    HazardLens.Cyclone,
                    displayName: definition.DisplayName,
                    wmsEndpoint: $"{mgb.ServicesRoot}/{definition.Service}/MapServer/WMSServer",
                    wmsLayerName: "0",
                    now)
                .Value
                .WithExplainer(definition.Explainer, definition.Note)
                .WithFeatureInfo(identifyEndpoint)
                .WithCachedTiles(cachedTileEndpoint)
                .WithPresentation(
                    isEnabledByDefault: false,
                    sortOrder: definition.SortOrder,
                    supportsFeatureInfo: true);

            context.HazardLayers.Add(layer);
        }
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
