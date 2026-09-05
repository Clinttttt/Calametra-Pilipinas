using Calametra.Domain.Abstractions;
using NetTopologySuite.Geometries;

namespace Calametra.Domain.Hazards;

public static class HazardFeatureErrors
{
    public static readonly Error SourceNotRedistributable =
        new(
            ErrorType.Forbidden,
            "hazard_feature.source_not_redistributable",
            "Geometry cannot be stored for a source that does not permit redistribution.");

    public static readonly Error ExternalIdRequired =
        new(
            ErrorType.Validation,
            "hazard_feature.external_id_required",
            "A stored feature must carry the publisher's own identifier so it can be reconciled.");
}

/// <summary>
/// One hazard feature whose geometry is stored locally.
/// </summary>
/// <remarks>
/// <para>
/// Only exists for sources that permit redistribution. The construction path
/// requires the caller to pass the owning source's redistribution flag and
/// refuses when it is false, so the licensing position cannot be bypassed by
/// forgetting to check it.
/// </para>
/// <para>
/// Stored geometry is what makes real spatial analysis possible: "which fault is
/// nearest this epicentre", "which faults lie within 25 km of this town". A
/// proxied raster layer can be looked at but not queried, so a locally stored
/// fault set is a genuinely different capability rather than an optimisation.
/// </para>
/// <para>
/// Publisher attributes are kept verbatim in <see cref="Attributes"/> rather than
/// mapped onto columns of our own. Different fault catalogues describe faults
/// differently — GEM records slip type, slip rate and dip; PHIVOLCS records
/// segment name, mapping year and project — and flattening both into one schema
/// would discard whichever fields did not fit.
/// </para>
/// </remarks>
public sealed class HazardFeature : AuditableEntity
{
    private HazardFeature()
    {
    }

    private HazardFeature(
        Guid id,
        Guid hazardLayerId,
        Guid dataSourceId,
        string externalId,
        Geometry geometry,
        DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        HazardLayerId = hazardLayerId;
        DataSourceId = dataSourceId;
        ExternalId = externalId;
        Geometry = geometry;
    }

    public Guid HazardLayerId { get; private set; }

    public Guid DataSourceId { get; private set; }

    /// <summary>The publisher's own identifier, e.g. GEM <c>PHL_96</c>.</summary>
    public string ExternalId { get; private set; } = string.Empty;

    /// <summary>Human-readable name, e.g. <c>Surigao Fault</c>.</summary>
    public string? Name { get; private set; }

    /// <summary>
    /// Classification in the publisher's own vocabulary, e.g. a slip type of
    /// <c>Sinistral</c>. Used for styling and filtering, never reinterpreted.
    /// </summary>
    public string? Classification { get; private set; }

    /// <summary>Trace, extent or point, depending on the hazard type.</summary>
    public Geometry Geometry { get; private set; } = default!;

    /// <summary>
    /// Remaining publisher attributes, serialised as JSON. Displayed verbatim in
    /// the feature inspector.
    /// </summary>
    public string? AttributesJson { get; private set; }

    public static Result<HazardFeature> Create(
        Guid hazardLayerId,
        Guid dataSourceId,
        string externalId,
        Geometry geometry,
        bool sourceIsRedistributable,
        DateTimeOffset now)
    {
        if (!sourceIsRedistributable)
        {
            return Result<HazardFeature>.Failure(HazardFeatureErrors.SourceNotRedistributable);
        }

        if (string.IsNullOrWhiteSpace(externalId))
        {
            return Result<HazardFeature>.Failure(HazardFeatureErrors.ExternalIdRequired);
        }

        var feature = new HazardFeature(
            Guid.CreateVersion7(),
            hazardLayerId,
            dataSourceId,
            externalId.Trim(),
            geometry,
            now);

        return Result<HazardFeature>.Success(feature);
    }

    public HazardFeature WithDescription(string? name, string? classification, string? attributesJson)
    {
        Name = name;
        Classification = classification;
        AttributesJson = attributesJson;
        return this;
    }

    /// <summary>Replaces geometry and attributes when the publisher revises the dataset.</summary>
    public void Revise(
        Geometry geometry,
        string? name,
        string? classification,
        string? attributesJson,
        DateTimeOffset now)
    {
        Geometry = geometry;
        Name = name;
        Classification = classification;
        AttributesJson = attributesJson;
        Touch(now);
    }
}
