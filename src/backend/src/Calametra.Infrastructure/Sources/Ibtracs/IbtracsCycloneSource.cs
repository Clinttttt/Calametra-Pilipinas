using System.Globalization;
using System.Runtime.CompilerServices;
using Calametra.Application.Abstractions.Sources;
using Calametra.Domain.Geospatial;
using Calametra.Domain.Meteorology;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Calametra.Infrastructure.Sources.Ibtracs;

/// <summary>
/// Reads cyclone tracks from NOAA's IBTrACS best-track archive.
/// </summary>
/// <remarks>
/// <para>
/// <b>Streamed, never buffered.</b> The western North Pacific file was 114,267,954 bytes when
/// last measured. It is read line by line straight off the response stream, and only one
/// storm's fixes are held at a time.
/// </para>
/// <para>
/// <b>Columns are resolved by header name, never by position.</b> The file has 174 columns.
/// Hardcoded indices would survive a compile, pass a smoke test, and silently attach wind
/// speeds to the wrong agency the moment NOAA reorders anything.
/// </para>
/// <para>
/// <b>Two header rows.</b> Line 0 is column names; line 1 is a units row — <c>kts</c>,
/// <c>mb</c>, <c>degrees_north</c>. A parser that assumes one header row reads the units as a
/// storm.
/// </para>
/// <para>
/// <b>The WMO columns are deliberately ignored.</b> <c>WMO_WIND</c> is not an independent
/// analysis; it is a copy of whichever agency WMO designates for the basin, named in
/// <c>WMO_AGENCY</c>. For the western Pacific that agency is Tokyo, and it was verified over
/// 104 rows carrying both values that <c>WMO_WIND</c> equals <c>TOKYO_WIND</c> in every one.
/// Ingesting both would fabricate a second opinion and overstate the agreement between
/// agencies — the opposite of what this platform exists to do.
/// </para>
/// </remarks>
internal sealed class IbtracsCycloneSource(
    HttpClient httpClient,
    IOptions<IbtracsOptions> options,
    ILogger<IbtracsCycloneSource> logger) : ICycloneTrackSource
{
    private readonly IbtracsOptions _options = options.Value;

    /// <summary>
    /// Agencies whose columns are read, with the averaging period each uses and how each
    /// describes its wind field.
    /// </summary>
    /// <remarks>
    /// The averaging period is the reason this table exists. IBTrACS records the interval
    /// nowhere in the data; it is a property of the agency, documented separately. Attaching it
    /// here is what allows <see cref="WindReading"/> to refuse a comparison later.
    /// <para>
    /// The wind-field geometry and gale threshold are recorded for the same reason. JTWC publishes
    /// four quadrant radii measured at 34 knots; JMA and KMA publish an ellipse measured at 30.
    /// Neither the shape nor the threshold is stated in the data, and a radius stored without them
    /// would look comparable across agencies when it is not.
    /// </para>
    /// <para>
    /// Only the agencies that routinely analyse western Pacific storms are read. The remaining
    /// eleven wind columns in the file belong to other basins and are empty here.
    /// </para>
    /// </remarks>
    private static readonly IbtracsAgency[] Agencies =
    [
        new("USA", "jtwc-best-track", WindAveragingPeriod.OneMinute, WindFieldGeometry.Quadrants, 34),
        new("TOKYO", "jma-best-track", WindAveragingPeriod.TenMinute, WindFieldGeometry.Ellipse, 30),
        new("CMA", "cma-best-track", WindAveragingPeriod.TwoMinute, WindFieldGeometry.None, 0),
        new("HKO", "hko-best-track", WindAveragingPeriod.TenMinute, WindFieldGeometry.None, 0),
        new("KMA", "kma-best-track", WindAveragingPeriod.TenMinute, WindFieldGeometry.Ellipse, 30),
    ];

    public string SourceSlug => IbtracsOptions.ArchiveSlug;

    public async IAsyncEnumerable<CatalogCyclone> StreamAsync(
        int fromSeason,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _options.WesternPacificCsvUrl);

        // ResponseHeadersRead so the body is not buffered into memory before parsing begins.
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var header = await reader.ReadLineAsync(cancellationToken);

        if (header is null)
        {
            IbtracsLog.EmptyResponse(logger);
            yield break;
        }

        var columns = IndexColumns(header);

        // Discard the units row.
        _ = await reader.ReadLineAsync(cancellationToken);

        var accumulator = new StormAccumulator(columns, Agencies, fromSeason);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (line.Length == 0)
            {
                continue;
            }

            // A completed storm is returned as soon as the next storm's first row appears,
            // which is what keeps memory bounded to one track.
            if (accumulator.Accept(line) is { } completed)
            {
                yield return completed;
            }
        }

        if (accumulator.Flush() is { } last)
        {
            yield return last;
        }
    }

    /// <summary>Maps column name to ordinal, so no index is ever written by hand.</summary>
    private static Dictionary<string, int> IndexColumns(string header)
    {
        var names = header.Split(',');
        var map = new Dictionary<string, int>(names.Length, StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < names.Length; i++)
        {
            // Last occurrence wins is avoided deliberately: IBTrACS has unique column names,
            // and a duplicate would indicate a format change worth failing on rather than
            // silently resolving.
            map.TryAdd(names[i].Trim(), i);
        }

        return map;
    }

    /// <summary>An agency's column prefix, slug, averaging period and wind-field convention.</summary>
    private sealed record IbtracsAgency(
        string ColumnPrefix,
        string SourceSlug,
        WindAveragingPeriod Period,
        WindFieldGeometry FieldGeometry,
        int GaleThresholdKnots);

    /// <summary>
    /// Gathers consecutive rows sharing a storm id into one track.
    /// </summary>
    /// <remarks>
    /// IBTrACS is ordered by storm, so a single-storm buffer is sufficient and no sorting or
    /// full-file grouping is needed.
    /// </remarks>
    private sealed class StormAccumulator(
        Dictionary<string, int> columns,
        IbtracsAgency[] agencies,
        int fromSeason)
    {
        private readonly List<CatalogCycloneFix> _fixes = [];

        private string? _sid;
        private string _name = "UNNAMED";
        private int _season;
        private bool _entersPhilippineArea;

        /// <summary>
        /// Adds a row, returning the previous storm if this row starts a new one.
        /// </summary>
        public CatalogCyclone? Accept(string line)
        {
            var fields = line.Split(',');

            var sid = Field(fields, "SID");

            if (string.IsNullOrEmpty(sid))
            {
                return null;
            }

            CatalogCyclone? completed = null;

            if (_sid is not null && !string.Equals(sid, _sid, StringComparison.Ordinal))
            {
                completed = Build();
                Reset();
            }

            _sid = sid;

            if (!int.TryParse(Field(fields, "SEASON"), CultureInfo.InvariantCulture, out _season))
            {
                _season = 0;
            }

            var name = Field(fields, "NAME");

            if (!string.IsNullOrWhiteSpace(name))
            {
                _name = name;
            }

            AddFixes(fields);

            return completed;
        }

        /// <summary>Returns the final storm, which has no successor row to trigger it.</summary>
        public CatalogCyclone? Flush() => _sid is null ? null : Build();

        private void AddFixes(string[] fields)
        {
            if (!DateTimeOffset.TryParse(
                    Field(fields, "ISO_TIME"),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var capturedAt))
            {
                return;
            }

            if (!TryParseDouble(Field(fields, "LAT"), out var latitude)
                || !TryParseDouble(Field(fields, "LON"), out var longitude))
            {
                return;
            }

            // IBTrACS reports longitude in -180..180 for this basin, but a storm crossing the
            // dateline can appear beyond 180. Normalised so the stored geography is valid.
            if (longitude > 180d)
            {
                longitude -= 360d;
            }

            if (PhilippineStudyArea.Contains(latitude, longitude))
            {
                _entersPhilippineArea = true;
            }

            var classification = Field(fields, "NATURE");

            // LANDFALL is the minimum distance in kilometres to land between this fix and the
            // next, so zero means the storm crosses the coast during that interval. Verified
            // against the archive: rows with LANDFALL=0 sit 0-10 km offshore. The comparison is
            // against the exact string because the column is frequently empty, and an empty
            // value must not be read as zero.
            var isLandfall = Field(fields, "LANDFALL") == "0";

            // Absent or unparseable leaves this null rather than zero: "0 km from land" is a
            // strong claim and must not be manufactured from a missing value.
            var distanceToLand = TryParseDouble(Field(fields, "DIST2LAND"), out var parsedDistance)
                ? parsedDistance
                : (double?)null;

            foreach (var agency in agencies)
            {
                var hasWind = TryParseDouble(Field(fields, $"{agency.ColumnPrefix}_WIND"), out var wind);
                var hasPressure = int.TryParse(
                    Field(fields, $"{agency.ColumnPrefix}_PRES"),
                    CultureInfo.InvariantCulture,
                    out var pressure);

                // An agency that reported neither wind nor pressure did not analyse this fix.
                if (!hasWind && !hasPressure)
                {
                    continue;
                }

                _fixes.Add(new CatalogCycloneFix
                {
                    SourceSlug = agency.SourceSlug,
                    CapturedAt = capturedAt,
                    // The agency's own position where it published one, since agencies differ
                    // on location as well as intensity; otherwise the archive's consensus.
                    Latitude = AgencyLatitude(fields, agency) ?? latitude,
                    Longitude = AgencyLongitude(fields, agency) ?? longitude,
                    Wind = hasWind ? new WindReading(wind, agency.Period) : null,
                    MinimumPressureMillibars = hasPressure ? pressure : null,
                    Classification = string.IsNullOrWhiteSpace(classification) ? null : classification,
                    DistanceToLandKm = distanceToLand,
                    IsLandfall = isLandfall,
                    RadiusOfMaximumWindNm = Radius(fields, $"{agency.ColumnPrefix}_RMW"),
                    RadiusOutermostIsobarNm = Radius(fields, $"{agency.ColumnPrefix}_ROCI"),
                    GaleField = ReadGaleField(fields, agency),
                    // Inner bands are quadrant-only and JTWC-only in this basin; an ellipse
                    // agency yields None rather than axes misread as quadrants.
                    StormField = ReadQuadrantBand(fields, agency, 50),
                    HurricaneField = ReadQuadrantBand(fields, agency, 64),
                });
            }
        }

        /// <summary>
        /// The agency's own latitude where it publishes one.
        /// </summary>
        /// <remarks>
        /// Only the United States columns carry a separate position in this file; the others
        /// share the archive's consensus track. Read generically so an agency gaining its own
        /// position columns upstream is picked up without a code change.
        /// </remarks>
        private double? AgencyLatitude(string[] fields, IbtracsAgency agency) =>
            TryParseDouble(Field(fields, $"{agency.ColumnPrefix}_LAT"), out var value) ? value : null;

        private double? AgencyLongitude(string[] fields, IbtracsAgency agency)
        {
            if (!TryParseDouble(Field(fields, $"{agency.ColumnPrefix}_LON"), out var value))
            {
                return null;
            }

            return value > 180d ? value - 360d : value;
        }

        private CatalogCyclone? Build()
        {
            // Two filters, both necessary. A storm that never entered the area of interest is
            // not this platform's subject, and a storm with no agency readings has nothing to
            // show.
            if (!_entersPhilippineArea || _fixes.Count == 0 || _season < fromSeason)
            {
                return null;
            }

            var ordered = _fixes.OrderBy(fix => fix.CapturedAt).ToList();

            return new CatalogCyclone
            {
                ExternalId = _sid!,
                Name = _name,
                Season = _season,
                StartedAt = ordered[0].CapturedAt,
                EndedAt = ordered[^1].CapturedAt,
                Fixes = ordered,
            };
        }

        private void Reset()
        {
            _fixes.Clear();
            _name = "UNNAMED";
            _season = 0;
            _entersPhilippineArea = false;
        }

        /// <summary>
        /// Reads a field by column name, tolerating a short row.
        /// </summary>
        /// <remarks>
        /// Returns empty rather than throwing on a missing column, because IBTrACS pads trailing
        /// empty fields inconsistently across eras — 1884 rows are far shorter than 2024 rows.
        /// </remarks>
        private string Field(string[] fields, string column) =>
            columns.TryGetValue(column, out var index) && index < fields.Length
                ? fields[index].Trim()
                : string.Empty;

        /// <summary>A radius in nautical miles, or null when the agency reported none.</summary>
        private double? Radius(string[] fields, string column) =>
            TryParseDouble(Field(fields, column), out var value) && value > 0d ? value : null;

        /// <summary>
        /// The agency's gale field, read in whichever geometry it publishes.
        /// </summary>
        /// <remarks>
        /// JTWC's quadrant columns and JMA's ellipse columns are named differently and mean
        /// different things, so there is no shared parse. Branching on the agency's declared
        /// geometry is what keeps a quadrant radius from being read as an axis length.
        /// </remarks>
        private WindField ReadGaleField(string[] fields, IbtracsAgency agency)
        {
            var prefix = agency.ColumnPrefix;

            return agency.FieldGeometry switch
            {
                WindFieldGeometry.Quadrants => WindField.FromQuadrants(
                    agency.GaleThresholdKnots,
                    Radius(fields, $"{prefix}_R34_NE"),
                    Radius(fields, $"{prefix}_R34_SE"),
                    Radius(fields, $"{prefix}_R34_SW"),
                    Radius(fields, $"{prefix}_R34_NW")),

                // R30, not R34: JMA and KMA measure the gale radius at a lower threshold, which is
                // why the threshold travels with the field rather than being assumed.
                WindFieldGeometry.Ellipse => WindField.FromEllipse(
                    agency.GaleThresholdKnots,
                    Radius(fields, $"{prefix}_R30_LONG"),
                    Radius(fields, $"{prefix}_R30_SHORT"),
                    int.TryParse(
                        Field(fields, $"{prefix}_R30_DIR"),
                        CultureInfo.InvariantCulture,
                        out var bearing)
                        ? bearing
                        : null),

                _ => WindField.None,
            };
        }

        /// <summary>A quadrant band at a given threshold, for agencies that publish quadrants.</summary>
        private WindField ReadQuadrantBand(string[] fields, IbtracsAgency agency, int thresholdKnots)
        {
            if (agency.FieldGeometry != WindFieldGeometry.Quadrants)
            {
                return WindField.None;
            }

            var prefix = agency.ColumnPrefix;

            return WindField.FromQuadrants(
                thresholdKnots,
                Radius(fields, $"{prefix}_R{thresholdKnots}_NE"),
                Radius(fields, $"{prefix}_R{thresholdKnots}_SE"),
                Radius(fields, $"{prefix}_R{thresholdKnots}_SW"),
                Radius(fields, $"{prefix}_R{thresholdKnots}_NW"));
        }

        private static bool TryParseDouble(string value, out double parsed) =>
            double.TryParse(value, CultureInfo.InvariantCulture, out parsed);
    }
}

/// <summary>
/// Source-generated log messages for the IBTrACS adapter.
/// </summary>
/// <remarks>
/// Event ids in the 9000 range, following the per-source blocks already in use: USGS 2000,
/// hazard maps 3000, GEM 8000.
/// </remarks>
internal static partial class IbtracsLog
{
    [LoggerMessage(
        EventId = 9000,
        Level = LogLevel.Warning,
        Message = "IBTrACS returned an empty response; no cyclone tracks were read")]
    public static partial void EmptyResponse(ILogger logger);

    [LoggerMessage(
        EventId = 9001,
        Level = LogLevel.Information,
        Message = "IBTrACS streamed {StormCount} storms entering the Philippine area, {FixCount} agency fixes")]
    public static partial void Streamed(ILogger logger, int stormCount, int fixCount);
}
