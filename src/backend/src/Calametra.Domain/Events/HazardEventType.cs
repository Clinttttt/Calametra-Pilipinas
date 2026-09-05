namespace Calametra.Domain.Events;

/// <summary>
/// The kind of natural hazard phenomenon an event represents.
/// </summary>
/// <remarks>
/// Named "hazard event" rather than "disaster event" deliberately. An M4.2
/// earthquake 60 km offshore is a hazard event and not a disaster; calling it one
/// would assert an impact Calametra has no data to support. The naming keeps the
/// platform inside the scientific boundaries set out in the project concept.
/// </remarks>
public enum HazardEventType
{
    Unknown = 0,

    /// <summary>Phase 1 scope.</summary>
    Earthquake = 1,

    /// <summary>Phase 3 scope. Modelled now because it drives the track-point design.</summary>
    TropicalCyclone = 2,
}
