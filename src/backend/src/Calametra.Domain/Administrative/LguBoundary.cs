using Calametra.Domain.Abstractions;
using NetTopologySuite.Geometries;

namespace Calametra.Domain.Administrative;

public static class LguBoundaryErrors
{
    public static readonly Error LguRequired = new(
        ErrorType.Validation,
        "lgu_boundary.lgu_required",
        "A boundary must name the canonical unit it outlines.");

    public static readonly Error SourceRequired = new(
        ErrorType.Validation,
        "lgu_boundary.source_required",
        "A boundary must name the data source it came from, so the licence and the extract travel with "
        + "the geometry.");

    public static readonly Error GeometryRequired = new(
        ErrorType.Validation,
        "lgu_boundary.geometry_required",
        "A boundary requires geometry.");

    public static readonly Error GeometryEmpty = new(
        ErrorType.Validation,
        "lgu_boundary.geometry_empty",
        "The geometry is empty. An empty outline is not a boundary and would make the unit look covered "
        + "while containing nothing.");

    public static readonly Error GeometryInvalid = new(
        ErrorType.Validation,
        "lgu_boundary.geometry_invalid",
        "The geometry is not valid and could not be repaired. Stored as nothing rather than as a shape "
        + "no containment test can be trusted against.");

    public static readonly Error AreaNotPositive = new(
        ErrorType.Validation,
        "lgu_boundary.area_not_positive",
        "A boundary must enclose a positive area.");

    public static readonly Error ExtractDateRequired = new(
        ErrorType.Validation,
        "lgu_boundary.extract_date_required",
        "A boundary must record the extract it was read from. ADR-005 D7 requires every containment "
        + "figure to be able to name the boundary edition behind it.");
}

/// <summary>
/// One version of one unit's outline, as read from one dated extract.
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-005 D7: geometry is never mutated in place.</b> LGU boundaries change by legislation rather
/// than by revision — provinces split, cities are created, regions are formed — so a new import
/// <em>supersedes</em> rather than overwrites, and the superseded row is retained. The consequence a
/// reader has to be able to see is that an event from 1976 attributed to a unit created in 2022 is
/// being attributed by today's boundary, which is only checkable if yesterday's boundary still exists.
/// </para>
/// <para>
/// <b>The geometry and its provenance are one record.</b> The OSM relation id, the <c>ref</c> tag as it
/// was actually observed, and the extract timestamp are held beside the polygon rather than in an
/// import log, because a boundary whose origin is only recoverable from logs is a boundary nobody can
/// audit once the logs rotate.
/// </para>
/// <para>
/// <b>Repair is recorded, not hidden.</b> OSM relations are assembled by volunteers from ways that can
/// self-intersect or fail to close, so a share of them are invalid under OGC rules. Silently dropping
/// those loses real municipalities; silently repairing them presents a guess as a measurement. So a
/// repaired geometry is stored with <see cref="WasRepaired"/> set and the reason recorded, and one that
/// cannot be repaired is refused.
/// </para>
/// </remarks>
public sealed class LguBoundary : AuditableEntity
{
    private LguBoundary()
    {
    }

    private LguBoundary(
        Guid id,
        Guid lguId,
        string canonicalPsgcCode,
        Guid sourceId,
        long osmRelationId,
        string? osmRefTag,
        string? osmName,
        int osmAdminLevel,
        MultiPolygon geometry,
        double areaSquareKm,
        DateTimeOffset extractedAt,
        string? extractVersion,
        bool wasRepaired,
        string? repairNote,
        DateTimeOffset now)
        : base(id, now)
    {
        LguId = lguId;
        CanonicalPsgcCode = canonicalPsgcCode;
        SourceId = sourceId;
        OsmRelationId = osmRelationId;
        OsmRefTag = osmRefTag;
        OsmName = osmName;
        OsmAdminLevel = osmAdminLevel;
        Geometry = geometry;
        AreaSquareKm = areaSquareKm;
        ExtractedAt = extractedAt;
        ExtractVersion = extractVersion;
        WasRepaired = wasRepaired;
        RepairNote = repairNote;
        ValidFrom = extractedAt;
    }

    public Guid LguId { get; private set; }

    /// <summary>
    /// The unit's ten-digit code, held beside the identifier.
    /// </summary>
    /// <remarks>
    /// Denormalised because a later edition may recode or retire the unit, and a retained boundary has
    /// to stay readable after that happens — which is the whole point of retaining it.
    /// </remarks>
    public string CanonicalPsgcCode { get; private set; } = string.Empty;

    public Guid SourceId { get; private set; }

    /// <summary>The OSM relation this outline was assembled from.</summary>
    public long OsmRelationId { get; private set; }

    /// <summary>
    /// The relation's <c>ref</c> tag exactly as observed, whatever edition of the code it held.
    /// </summary>
    /// <remarks>
    /// Stored verbatim rather than normalised. The Philippine OSM convention records the PSGC in
    /// <c>ref</c>, but the documented convention says nine digits while the mapped data is largely ten,
    /// so what a relation actually carried is a fact worth keeping — it is the evidence for how this
    /// boundary was attached to this unit.
    /// </remarks>
    public string? OsmRefTag { get; private set; }

    /// <summary>What OSM calls the unit. Recorded for review, never used to establish identity.</summary>
    public string? OsmName { get; private set; }

    public int OsmAdminLevel { get; private set; }

    public MultiPolygon Geometry { get; private set; } = null!;

    /// <summary>
    /// Area in square kilometres, computed on the geodetic surface at import.
    /// </summary>
    /// <remarks>
    /// Stored rather than computed per query because ADR-005 D1 requires any figure derived from a
    /// boundary to state the unit's area beside it: Philippine LGUs differ in area by more than two
    /// orders of magnitude, so a count inside a boundary encodes land area unless the area is shown.
    /// </remarks>
    public double AreaSquareKm { get; private set; }

    /// <summary>When the extract this came from was taken.</summary>
    public DateTimeOffset ExtractedAt { get; private set; }

    /// <summary>
    /// The upstream's own statement of what it served — for Overpass, its <c>timestamp_osm_base</c>.
    /// </summary>
    public string? ExtractVersion { get; private set; }

    public bool WasRepaired { get; private set; }

    public string? RepairNote { get; private set; }

    /// <summary>When this version came into force in this platform's record.</summary>
    public DateTimeOffset ValidFrom { get; private set; }

    /// <summary>Null while this is the boundary in force.</summary>
    public DateTimeOffset? ValidTo { get; private set; }

    public Guid? SupersededByBoundaryId { get; private set; }

    /// <summary>The boundary a containment query should use.</summary>
    public bool IsInForce => ValidTo is null;

    /// <summary>
    /// Records one version of one unit's outline.
    /// </summary>
    /// <param name="geometry">
    /// Must already be valid. Repair belongs to the adapter that assembled it, which is the only place
    /// that knows what it did; this entity records that a repair happened, not how.
    /// </param>
    public static Result<LguBoundary> Record(
        Guid lguId,
        string canonicalPsgcCode,
        Guid sourceId,
        long osmRelationId,
        string? osmRefTag,
        string? osmName,
        int osmAdminLevel,
        MultiPolygon? geometry,
        double areaSquareKm,
        DateTimeOffset extractedAt,
        string? extractVersion,
        bool wasRepaired,
        string? repairNote,
        DateTimeOffset now)
    {
        if (lguId == Guid.Empty || string.IsNullOrWhiteSpace(canonicalPsgcCode))
        {
            return Result<LguBoundary>.Failure(LguBoundaryErrors.LguRequired);
        }

        if (sourceId == Guid.Empty)
        {
            return Result<LguBoundary>.Failure(LguBoundaryErrors.SourceRequired);
        }

        if (geometry is null)
        {
            return Result<LguBoundary>.Failure(LguBoundaryErrors.GeometryRequired);
        }

        if (geometry.IsEmpty)
        {
            return Result<LguBoundary>.Failure(LguBoundaryErrors.GeometryEmpty);
        }

        if (!geometry.IsValid)
        {
            return Result<LguBoundary>.Failure(LguBoundaryErrors.GeometryInvalid);
        }

        if (areaSquareKm <= 0)
        {
            return Result<LguBoundary>.Failure(LguBoundaryErrors.AreaNotPositive);
        }

        if (extractedAt == default)
        {
            return Result<LguBoundary>.Failure(LguBoundaryErrors.ExtractDateRequired);
        }

        return Result<LguBoundary>.Success(new LguBoundary(
            Guid.CreateVersion7(),
            lguId,
            canonicalPsgcCode.Trim(),
            sourceId,
            osmRelationId,
            string.IsNullOrWhiteSpace(osmRefTag) ? null : osmRefTag.Trim(),
            string.IsNullOrWhiteSpace(osmName) ? null : osmName.Trim(),
            osmAdminLevel,
            geometry,
            areaSquareKm,
            extractedAt,
            string.IsNullOrWhiteSpace(extractVersion) ? null : extractVersion.Trim(),
            wasRepaired,
            string.IsNullOrWhiteSpace(repairNote) ? null : repairNote.Trim(),
            now));
    }

    /// <summary>
    /// Retires this version in favour of a later one. One-way, and idempotent.
    /// </summary>
    /// <remarks>
    /// The geometry is untouched. That is the entire point of D7: a figure published last year was
    /// computed against this shape, and deleting or editing it would make that figure unexplainable.
    /// </remarks>
    public Result Supersede(Guid supersededByBoundaryId, DateTimeOffset now)
    {
        if (ValidTo is not null)
        {
            return Result.Success();
        }

        if (supersededByBoundaryId == Guid.Empty || supersededByBoundaryId == Id)
        {
            return Result.Success();
        }

        ValidTo = now;
        SupersededByBoundaryId = supersededByBoundaryId;
        Touch(now);

        return Result.Success();
    }
}
