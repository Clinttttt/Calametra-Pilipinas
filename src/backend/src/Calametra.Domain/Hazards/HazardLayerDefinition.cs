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
    /// Template for a pre-rendered tile, with <c>{z}</c>, <c>{x}</c> and <c>{y}</c> placeholders.
    /// Null when the publisher offers no cache and every tile must be rendered on demand.
    /// </summary>
    /// <remarks>
    /// Held because the difference is not cosmetic. Measured on 2026-09-11: DOST-MGB renders a
    /// 256 px susceptibility tile through WMS in 18.8-19.4 seconds and serves the same tile from
    /// its published cache in 60-120 milliseconds. A layer without the cached path is unusable
    /// for panning and puts a fresh render on the agency's server for every tile a reader crosses.
    /// PHIVOLCS publishes no cache for its hazard services, so those layers keep the rendered
    /// path — which is why this is per layer rather than a platform-wide switch.
    /// </remarks>
    public string? CachedTileEndpoint { get; private set; }

    /// <summary>Whether a pre-rendered tile cache is available for this layer.</summary>
    public bool SupportsCachedTiles => !string.IsNullOrWhiteSpace(CachedTileEndpoint);

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

    /// <summary>
    /// Zoom below which this layer must not be requested at all, or null for no limit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A property of the publisher's service rather than a rendering preference, which is why it is
    /// stored per layer. A service that renders every tile on demand costs time proportional to the
    /// polygons intersecting the view, so the same layer can be instant close in and unusable at
    /// national zoom. Measured on 2026-09-12 against PHIVOLCS: ground shaking renders a 400 km extent
    /// in 2.9 s and a 1,300 km one — a single tile at the opening view — in 19.1 s, and
    /// earthquake-induced landslide takes 32.8 s at a 78 km tile against 8.1 s at 39 km.
    /// </para>
    /// <para>
    /// Enforced by the client, which is the only place it can be: a bounding-box request carries no
    /// zoom, so the proxy cannot tell a national tile from a local one. MapLibre honours it by not
    /// issuing the request, so a layer below its minimum costs the publisher nothing.
    /// </para>
    /// <para>
    /// <b>Not a substitute for refusing a layer outright.</b> The tsunami inundation service takes
    /// 139-143 s reproducibly for a 20 km tile over Manila Bay — dense mapping rather than a wide
    /// view — so no minimum zoom makes it usable, and it stays out of the catalogue.
    /// </para>
    /// </remarks>
    public int? MinimumZoom { get; private set; }

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
    /// Sets the zoom below which the layer must not be requested.
    /// </summary>
    /// <remarks>
    /// Additive and separate from <see cref="WithPresentation"/>, matching
    /// <see cref="WithCachedTiles"/>: both record a measured property of the publisher's service
    /// rather than a choice about how this platform presents it, and most layers need neither.
    /// </remarks>
    public HazardLayerDefinition WithMinimumZoom(int minimumZoom)
    {
        MinimumZoom = minimumZoom;
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
    /// Records that the publisher exposes a pre-rendered tile cache, and where.
    /// </summary>
    /// <param name="cachedTileEndpoint">
    /// A template containing <c>{z}</c>, <c>{x}</c> and <c>{y}</c>. The caller is responsible for
    /// ordering the placeholders to match the publisher's scheme — ArcGIS addresses a cached tile
    /// as <c>level/row/column</c>, which is <c>{z}/{y}/{x}</c>.
    /// </param>
    public HazardLayerDefinition WithCachedTiles(string cachedTileEndpoint)
    {
        CachedTileEndpoint = cachedTileEndpoint;
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
