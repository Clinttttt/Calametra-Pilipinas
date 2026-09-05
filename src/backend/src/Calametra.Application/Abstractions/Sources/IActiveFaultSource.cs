using Calametra.Domain.Abstractions;
using NetTopologySuite.Geometries;

namespace Calametra.Application.Abstractions.Sources;

/// <summary>A mapped fault trace as published by an upstream fault catalogue.</summary>
/// <remarks>
/// Carries an NTS <see cref="Geometry"/> directly. Permitted because geometry is
/// domain vocabulary in this system (ADR-002), and converting to a neutral
/// coordinate list here would only be converted straight back by the handler.
/// </remarks>
public sealed record CatalogFault
{
    /// <summary>The publisher's own identifier, e.g. GEM <c>PHL_96</c>.</summary>
    public required string ExternalId { get; init; }

    /// <summary>Fault name as published, e.g. <c>Surigao Fault</c>.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Kinematic classification in the publisher's vocabulary — <c>Sinistral</c>,
    /// <c>Dextral</c>, <c>Reverse</c>. Used for styling, never reinterpreted.
    /// </summary>
    public string? SlipType { get; init; }

    /// <summary>The mapped trace.</summary>
    public required Geometry Trace { get; init; }

    /// <summary>Remaining publisher attributes, preserved verbatim.</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>A bounded request for fault traces.</summary>
public sealed record ActiveFaultQuery
{
    public required double MinLatitude { get; init; }

    public required double MaxLatitude { get; init; }

    public required double MinLongitude { get; init; }

    public required double MaxLongitude { get; init; }
}

/// <summary>
/// Port for a catalogue of mapped active faults whose geometry may be stored.
/// </summary>
/// <remarks>
/// Separate from <see cref="IHazardMapService"/> because the two answer different
/// questions. A map service can be displayed but not queried; a fault source
/// yields geometry that goes into PostGIS, which is what makes "nearest fault to
/// this epicentre" answerable.
/// <para>
/// Modelled as a port with several implementations expected. GEM is used now
/// because it is openly licensed; PHIVOLCS becomes an additional implementation
/// if the academic data request is approved. Both can coexist as distinct
/// <c>DataSource</c> rows, which lets the interface show an authoritative
/// national dataset alongside an open global one rather than silently replacing
/// one with the other.
/// </para>
/// </remarks>
public interface IActiveFaultSource
{
    /// <summary>Slug of the <c>DataSource</c> this adapter reads from.</summary>
    string SourceSlug { get; }

    Task<Result<IReadOnlyList<CatalogFault>>> FetchAsync(
        ActiveFaultQuery query,
        CancellationToken cancellationToken);
}
