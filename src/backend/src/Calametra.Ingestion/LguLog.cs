using Microsoft.Extensions.Logging;

namespace Calametra.Ingestion;

/// <summary>
/// Log messages for the ADR-005 administrative identity modes.
/// </summary>
/// <remarks>
/// Separate from <c>WorkerLog</c> because these belong to one-shot operator commands rather than to the
/// scheduled worker, and because the event id range keeps them findable: 7200 upwards is the crosswalk.
/// </remarks>
internal static partial class LguLog
{
    [LoggerMessage(
        EventId = 7200,
        Level = LogLevel.Information,
        Message = "PSGC register '{EditionLabel}' loaded as {Provenance} (may certify a crosswalk: "
            + "{IsCitable}). Observed {Regions} regions, {Provinces} provinces, {Cities} cities, "
            + "{Municipalities} municipalities. {Created} units created, {Reconciled} reconciled, "
            + "{Rejected} rejected. {StatedPairings} units carry the register's own nine-digit pairing.")]
    public static partial void RegisterImportCompleted(
        ILogger logger,
        string editionLabel,
        string provenance,
        bool isCitable,
        int regions,
        int provinces,
        int cities,
        int municipalities,
        int created,
        int reconciled,
        int rejected,
        int statedPairings);

    [LoggerMessage(
        EventId = 7201,
        Level = LogLevel.Error,
        Message = "PSGC register import failed: {ErrorCode} — {ErrorDescription}")]
    public static partial void RegisterImportFailed(
        ILogger logger,
        string errorCode,
        string errorDescription);

    [LoggerMessage(
        EventId = 7202,
        Level = LogLevel.Information,
        Message = "Matcher run over {RegisterUnits} register units and {DirectoryRows} directory rows: "
            + "{RegisterMatch} proposed on the register's own pairing, {Reslice} on digit re-slicing "
            + "({NameDisagreement} with disagreeing names, unconfirmable on that evidence), "
            + "{AlreadyProposed} already present. {UnitsWithNoCandidate} units and "
            + "{DirectoryWithNoCandidate} directory rows had no candidate. Every row written is Proposed.")]
    public static partial void ProposalCompleted(
        ILogger logger,
        int registerUnits,
        int directoryRows,
        int registerMatch,
        int reslice,
        int nameDisagreement,
        int alreadyProposed,
        int unitsWithNoCandidate,
        int directoryWithNoCandidate);

    [LoggerMessage(
        EventId = 7203,
        Level = LogLevel.Error,
        Message = "Pairing proposal failed: {ErrorCode} — {ErrorDescription}")]
    public static partial void ProposalFailed(
        ILogger logger,
        string errorCode,
        string errorDescription);
}
