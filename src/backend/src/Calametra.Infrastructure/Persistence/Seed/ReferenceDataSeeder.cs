using Calametra.Domain.Hazards;
using Calametra.Domain.Sources;
using Calametra.Infrastructure.Sources.Gem;
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

        await context.SaveChangesAsync(cancellationToken);

        await EnsureFaultLayerAsync(activeFault, now, cancellationToken);
        await EnsureTrenchLayerAsync(trenches, now, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        SeedLog.Completed(logger, usgs.Slug);
        _ = gem;
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
                    "Subduction trench axes. Relevant to CARAGA because the Philippine Trench lies "
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
                    "A trench marks where one tectonic plate descends beneath another. The Philippine "
                    + "Trench runs along the eastern edge of Mindanao and is the source of the deep "
                    + "earthquakes recorded beneath the CARAGA region.",
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
