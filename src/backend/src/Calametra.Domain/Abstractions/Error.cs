namespace Calametra.Domain.Abstractions;

/// <summary>
/// A named, expected failure. Declared once as a static readonly field on an
/// aggregate's <c>{Aggregate}Errors</c> class so tests can assert on identity
/// (<c>result.Error == EventErrors.NotFound</c>) rather than matching prose.
/// </summary>
/// <param name="Type">Drives HTTP status mapping in Calametra.Api.</param>
/// <param name="Code">Stable machine-readable key, e.g. <c>event.not_found</c>.</param>
/// <param name="Description">Human-readable message. Safe to change without breaking tests.</param>
public sealed record Error(ErrorType Type, string Code, string Description)
{
    public static readonly Error None = new(ErrorType.Failure, string.Empty, string.Empty);

    public override string ToString() => Code;
}
