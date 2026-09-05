using Calametra.Domain.Abstractions;

namespace Calametra.Domain.Hazards;

/// <summary>
/// A hazard layer Calametra can display: where it comes from, how it is delivered,
/// and how it should be explained to a non-specialist.
/// </summary>
/// <remarks>
/// This is a catalogue entry, not a geometry store. The distinction matters because
/// of how PHIVOLCS publishes data. Their public ArcGIS services expose map
/// rendering and per-feature inspection, but bulk vector query is disabled — so in
/// V1 the active-fault layer is rendered by proxying the publisher's own imagery
/// (<see cref="LayerDeliveryMode.RemoteWms"/>) and clicking a fault performs a
/// live attribute lookup. Nothing is copied.
/// <para>
/// If and when a dataset is formally granted, the same catalogue entry switches to
/// <see cref="LayerDeliveryMode.LocalVector"/> and geometry moves into PostGIS.
/// The switch is guarded: it requires the owning source to be marked
/// redistributable, so the licensing position is enforced by the model rather than
/// remembered by a developer.
/// </para>
/// </remarks>
public sealed class HazardLayerDefinition : AuditableEntity
{
    private HazardLayerDefinition()
    {
    }

    private HazardLayerDefinition(
        Guid id,
        Guid dataSourceId,
        HazardType hazardType,
        HazardLens lens,
        string displayName,
        LayerDeliveryMode deliveryMode,
        DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        DataSourceId = dataSourceId;
        HazardType = hazardType;
        Lens = lens;
        DisplayName = displayName;
        DeliveryMode = deliveryMode;
    }

    public Guid DataSourceId { get; private set; }

    public HazardType HazardType { get; private set; }

    /// <summary>Which lens surfaces this layer.</summary>
    public HazardLens Lens { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public LayerDeliveryMode DeliveryMode { get; private set; }

    /// <summary>WMS service endpoint, when delivered remotely.</summary>
    public string? WmsEndpoint { get; private set; }

    /// <summary>WMS layer identifier, when delivered remotely.</summary>
    public string? WmsLayerName { get; private set; }

    /// <summary>
    /// Endpoint used to inspect a single feature, when it differs from the
    /// rendering endpoint.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="WmsEndpoint"/> because on the PHIVOLCS ArcGIS
    /// service the two are genuinely different services. WMS <c>GetFeatureInfo</c>
    /// advertises <c>application/geojson</c> and returns an empty collection for
    /// every request, including a one-degree window; the ArcGIS REST
    /// <c>identify</c> operation on the same layer returns the fault system,
    /// segment name and year mapped. Verified 2026-09-04.
    /// <para>
    /// Storing it rather than deriving it from the WMS URL by string surgery keeps
    /// the two independent, so a source whose inspection lives somewhere else
    /// entirely needs no code change.
    /// </para>
    /// </remarks>
    public string? FeatureInfoEndpoint { get; private set; }

    /// <summary>
    /// Whether the service answers <c>GetFeatureInfo</c>, which determines if
    /// clicking a feature can reveal official attributes.
    /// </summary>
    public bool SupportsFeatureInfo { get; private set; }

    /// <summary>
    /// Short explanation of what the layer means, in plain language, for the
    /// "Explain this map" panel. Susceptibility is not prediction, and this text is
    /// where that is said.
    /// </summary>
    public string? Explainer { get; private set; }

    /// <summary>What a highlighted area does and does not imply.</summary>
    public string? InterpretationNote { get; private set; }

    public bool IsEnabledByDefault { get; private set; }

    public int SortOrder { get; private set; }

    public static Result<HazardLayerDefinition> CreateRemote(
        Guid dataSourceId,
        HazardType hazardType,
        HazardLens lens,
        string displayName,
        string wmsEndpoint,
        string wmsLayerName,
        DateTimeOffset now)
    {
        if (hazardType == HazardType.Unknown)
        {
            return Result<HazardLayerDefinition>.Failure(HazardLayerErrors.UnknownType);
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Result<HazardLayerDefinition>.Failure(HazardLayerErrors.DisplayNameRequired);
        }

        if (string.IsNullOrWhiteSpace(wmsEndpoint) || string.IsNullOrWhiteSpace(wmsLayerName))
        {
            return Result<HazardLayerDefinition>.Failure(HazardLayerErrors.WmsConfigurationRequired);
        }

        var definition = new HazardLayerDefinition(
            Guid.CreateVersion7(),
            dataSourceId,
            hazardType,
            lens,
            displayName.Trim(),
            LayerDeliveryMode.RemoteWms,
            now)
        {
            WmsEndpoint = wmsEndpoint.Trim(),
            WmsLayerName = wmsLayerName.Trim(),
        };

        return Result<HazardLayerDefinition>.Success(definition);
    }

    public HazardLayerDefinition WithExplainer(string? explainer, string? interpretationNote)
    {
        Explainer = explainer;
        InterpretationNote = interpretationNote;
        return this;
    }

    /// <summary>
    /// Creates a layer whose geometry Calametra stores and serves itself.
    /// </summary>
    /// <param name="sourceIsRedistributable">
    /// Taken from the owning <c>DataSource</c>. Passed in rather than looked up so
    /// the domain stays free of data access. Creation is refused when false.
    /// </param>
    public static Result<HazardLayerDefinition> CreateLocal(
        Guid dataSourceId,
        HazardType hazardType,
        HazardLens lens,
        string displayName,
        bool sourceIsRedistributable,
        DateTimeOffset now)
    {
        if (!sourceIsRedistributable)
        {
            return Result<HazardLayerDefinition>.Failure(HazardLayerErrors.LocalStorageNotPermitted);
        }

        if (hazardType == HazardType.Unknown)
        {
            return Result<HazardLayerDefinition>.Failure(HazardLayerErrors.UnknownType);
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Result<HazardLayerDefinition>.Failure(HazardLayerErrors.DisplayNameRequired);
        }

        var definition = new HazardLayerDefinition(
            Guid.CreateVersion7(),
            dataSourceId,
            hazardType,
            lens,
            displayName.Trim(),
            LayerDeliveryMode.LocalVector,
            now);

        return Result<HazardLayerDefinition>.Success(definition);
    }

    public HazardLayerDefinition WithPresentation(
        bool isEnabledByDefault,
        int sortOrder,
        bool supportsFeatureInfo)
    {
        IsEnabledByDefault = isEnabledByDefault;
        SortOrder = sortOrder;
        SupportsFeatureInfo = supportsFeatureInfo;
        return this;
    }

    /// <summary>
    /// Sets the endpoint used for per-feature inspection and marks the layer as
    /// inspectable.
    /// </summary>
    public HazardLayerDefinition WithFeatureInfo(string featureInfoEndpoint)
    {
        FeatureInfoEndpoint = featureInfoEndpoint;
        SupportsFeatureInfo = true;
        return this;
    }

    /// <summary>
    /// Promotes the layer to locally stored vector delivery. Refused unless the
    /// owning source permits redistribution.
    /// </summary>
    /// <param name="sourceIsRedistributable">
    /// Taken from the owning <c>DataSource</c>. Passed in rather than looked up so
    /// the domain stays free of data access.
    /// </param>
    public Result PromoteToLocalVector(bool sourceIsRedistributable, DateTimeOffset now)
    {
        if (!sourceIsRedistributable)
        {
            return Result.Failure(HazardLayerErrors.LocalStorageNotPermitted);
        }

        DeliveryMode = LayerDeliveryMode.LocalVector;
        Touch(now);

        return Result.Success();
    }
}
