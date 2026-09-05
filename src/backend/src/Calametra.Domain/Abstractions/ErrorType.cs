namespace Calametra.Domain.Abstractions;

/// <summary>
/// Classifies an expected failure so the transport layer can map it without
/// knowing anything about the domain. Mapped to HTTP in Calametra.Api only.
/// </summary>
public enum ErrorType
{
    Failure = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Unauthorized = 4,
    Forbidden = 5,
}
