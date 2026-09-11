using Calametra.Domain.Seismology;

namespace Calametra.Application.Features.Earthquakes.Shared;

/// <summary>A magnitude value that cannot be rendered without its scale.</summary>
public sealed record MagnitudeResponse(
    double Value,
    string Scale,
    string ScaleFamily,
    string Display)
{
    public static MagnitudeResponse? From(double? value, MagnitudeType scale) =>
        value is { } magnitude
            ? new MagnitudeResponse(
                magnitude,
                scale.Label(),
                scale.Family().ToString(),
                $"{scale.Label()} {magnitude:0.0}")
            : null;
}

/// <summary>
/// A depth value that carries whether it was measured.
/// </summary>
/// <remarks>
/// <see cref="IsMeasured"/> is what the cross-section and the depth filters key on.
/// Roughly 43% of USGS Philippine events report an agency default depth of exactly
/// 10 km or 35 km, so a client that ignored this flag would draw two false
/// horizontal bands of hypocentres.
/// </remarks>
public sealed record DepthResponse(
    double? Kilometres,
    string Quality,
    bool IsMeasured,
    string Display)
{
    public static DepthResponse From(double? kilometres, DepthQuality quality)
    {
        var reading = new DepthReading(kilometres, quality);

        return new DepthResponse(
            kilometres,
            quality.ToString(),
            reading.IsQuantitative,
            reading.Display());
    }
}

/// <summary>One agency's reading, with the agency named.</summary>
public sealed record ObservationResponse(
    Guid Id,
    string Agency,
    string DatasetName,
    string SourceSlug,
    string ExternalId,
    DateTimeOffset ObservedAt,
    double Latitude,
    double Longitude,
    MagnitudeResponse? Magnitude,
    DepthResponse Depth,
    bool IsPreferred,
    string? SourceUrl);

/// <summary>
/// An earthquake as shown in lists, on the map and on the timeline.
/// </summary>
/// <remarks>
/// Provenance travels with every record rather than being fetched separately. A
/// magnitude without its agency and scale is not displayable in this system, so the
/// summary shape makes it impossible for a client to render one by accident.
/// </remarks>
public sealed record EarthquakeSummaryResponse(
    Guid Id,
    DateTimeOffset OccurredAt,
    double Latitude,
    double Longitude,
    MagnitudeResponse? Magnitude,
    DepthResponse Depth,
    string SourceAgency,
    string SourceSlug,
    bool HasMultipleObservations,
    bool HasMagnitudeDisagreement);

/// <summary>An earthquake with every agency reading and the disagreement made explicit.</summary>
/// <param name="Location">
/// The epicentre stated relative to the nearest city or municipality, e.g.
/// <c>21 km NNW of Surigao City</c>. Null when the nearest is further than 300 km — 66 of the
/// 27,241 catalogued events, all deep-ocean — or when the place directory has not been imported.
/// <para>
/// The distance is to the gazetteer's representative point for the place, not to its boundary,
/// which is the same convention the USGS uses for its own place field. No boundary is stored for
/// Philippine local government units.
/// </para>
/// </param>
public sealed record EarthquakeDetailResponse(
    Guid Id,
    DateTimeOffset OccurredAt,
    double Latitude,
    double Longitude,
    string? Location,
    IReadOnlyList<ObservationResponse> Observations,
    bool HasMagnitudeDisagreement,
    string? DisagreementExplanation);
