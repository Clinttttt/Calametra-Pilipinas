using System.Globalization;
using System.Text;
using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Administrative;
using Calametra.Domain.Places;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Calametra.Application.Features.Administrative;

/// <summary>
/// Proposes crosswalk pairings. Cannot establish one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every row this writes is <c>Proposed</c>, and there is no code path here that writes anything
/// else.</b> That is ADR-005 D4: matching may propose a pairing, only reviewed evidence may establish
/// one. Refusing machine matching outright would be theatre — 1,750 rows cannot be paired by hand from
/// nothing — so the matcher does the bulk work and the review gate does the deciding.
/// </para>
/// <para>
/// <b>Two rules fire, and their strength is not equal.</b> Where the register itself publishes both
/// editions of a code, the proposal is marked <c>RegisterMatch</c> and needs no interpretation. Where it
/// does not, the matcher falls back to re-slicing the digits and comparing names, and marks the proposal
/// <c>DigitReslice</c> — which the domain will only allow a reviewer to confirm when the names agree,
/// because that is the rule that fails in the capital.
/// </para>
/// <para>
/// <b>Name comparison is deliberately conservative.</b> It normalises case, diacritics, punctuation and
/// the <c>City of</c> / <c>City</c> affixes the two publishers disagree about, and nothing else. It does
/// not do fuzzy distance: a matcher that guessed at 85% similarity would produce proposals a reviewer
/// cannot check quickly, and the point of a proposal is to save review effort rather than to create it.
/// </para>
/// </remarks>
public static class ProposeLguCodeLinks
{
    /// <summary>The matcher's identity, recorded on every proposal so a run can be audited.</summary>
    public const string MatcherName = "calametra.lgu-matcher/1.0";

    public sealed record Command : ICommand<ProposalSummary>;

    /// <param name="RegisterUnits">Canonical units held, the population being paired.</param>
    /// <param name="DirectoryRows">Gazetteer rows carrying a nine-digit code, the other side.</param>
    /// <param name="ProposedRegisterMatch">
    /// Proposals where the register itself states both codes. The strongest evidence available.
    /// </param>
    /// <param name="ProposedDigitReslice">
    /// Proposals resting on a documented re-slicing. Confirmable only where the names also agree.
    /// </param>
    /// <param name="ResliceWithNameDisagreement">
    /// Re-slicings whose names do NOT agree. Proposed anyway, so a reviewer sees them, but the domain
    /// will refuse to confirm them on that evidence — they must go to manual review or be rejected.
    /// </param>
    /// <param name="AlreadyProposed">Pairings this run found already present. The matcher is idempotent.</param>
    /// <param name="UnitsWithNoCandidate">
    /// Canonical units the matcher could propose nothing for. Not a defect: the register may hold a unit
    /// the directory does not, and ADR-005 requires this number to be enumerated and accepted rather
    /// than driven to zero.
    /// </param>
    public sealed record ProposalSummary(
        int RegisterUnits,
        int DirectoryRows,
        int ProposedRegisterMatch,
        int ProposedDigitReslice,
        int ResliceWithNameDisagreement,
        int AlreadyProposed,
        int UnitsWithNoCandidate,
        int DirectoryRowsWithNoCandidate);

    internal sealed class Handler(
        ILguCrosswalkReviewContext context,
        IApplicationDbContext analytics,
        TimeProvider timeProvider,
        ILogger<Handler> logger) : ICommandHandler<Command, ProposalSummary>
    {
        private static readonly Error NoRegisterLoaded = new(
            ErrorType.Validation,
            "lgu_matcher.no_register",
            "No canonical units are held. The PSGC register must be imported before pairings can be "
            + "proposed.");

        public async Task<Result<ProposalSummary>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            var now = timeProvider.GetUtcNow();

            var units = await context.Lgus.ToListAsync(cancellationToken);

            if (units.Count == 0)
            {
                return Result<ProposalSummary>.Failure(NoRegisterLoaded);
            }

            // Only the tiers this platform holds as identities. Barangays are out of scope by ADR-005,
            // and a directory row with no code cannot be paired on a code at all.
            var directory = await analytics.Places
                .Where(place => place.PsgcCode != null && place.Kind != PlaceKind.Barangay)
                .Select(place => new DirectoryRow(place.Id, place.PsgcCode!, place.Name, place.Kind))
                .ToListAsync(cancellationToken);

            // Grouped rather than dictionaried: 111 city and municipality names are not unique, and the
            // nine-digit code is not guaranteed unique in the directory either. A duplicate must produce
            // two proposals for a reviewer to separate, never a silent first-wins.
            var byHistoricalCode = directory
                .GroupBy(row => row.PsgcCode, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

            var existingKeys = (await context.LguCodeLinks
                    .Select(link => new { link.LguId, link.HistoricalPsgcCode })
                    .ToListAsync(cancellationToken))
                .Select(link => (link.LguId, link.HistoricalPsgcCode))
                .ToHashSet();

            var registerMatch = 0;
            var reslice = 0;
            var resliceNameDisagreement = 0;
            var alreadyProposed = 0;
            var unitsWithNoCandidate = 0;
            var pairedDirectoryRows = new HashSet<Guid>();

            foreach (var unit in units)
            {
                // Rule 1: the register states the pairing itself. Nothing is inferred.
                // Rule 2: re-slice the ten-digit code into the nine-digit form. Documented, and wrong in
                // Metro Manila, which is why the evidence value differs and the domain guards it.
                var stated = unit.RegisterStatedHistoricalCode;
                var candidateCode = stated ?? ResliceToNineDigits(unit.CanonicalPsgcCode);
                var evidence = stated is not null
                    ? LguLinkEvidence.RegisterMatch
                    : LguLinkEvidence.DigitReslice;

                if (candidateCode is null || !byHistoricalCode.TryGetValue(candidateCode, out var rows))
                {
                    unitsWithNoCandidate++;

                    continue;
                }

                foreach (var row in rows)
                {
                    if (!existingKeys.Add((unit.Id, candidateCode)))
                    {
                        alreadyProposed++;
                        pairedDirectoryRows.Add(row.PlaceId);

                        continue;
                    }

                    var namesAgree = NamesAgree(unit.Name, row.Name);

                    var basis = string.Create(
                        CultureInfo.InvariantCulture,
                        $"{evidence}: register {unit.CanonicalPsgcCode} '{unit.Name}' against directory "
                        + $"{candidateCode} '{row.Name}'; names {(namesAgree ? "agree" : "DISAGREE")}");

                    var proposal = LguCodeLink.Propose(
                        unit.Id,
                        candidateCode,
                        row.PlaceId,
                        evidence,
                        basis,
                        namesAgree,
                        MatcherName,
                        now);

                    if (proposal.IsFailure)
                    {
                        // A proposal the domain refuses is a matcher defect, not a register property, so
                        // it is logged loudly rather than folded into a count.
                        MatcherLog.ProposalRefused(
                            logger,
                            unit.CanonicalPsgcCode,
                            candidateCode,
                            proposal.Error!.Code);

                        continue;
                    }

                    context.LguCodeLinks.Add(proposal.Value);
                    pairedDirectoryRows.Add(row.PlaceId);

                    if (evidence == LguLinkEvidence.RegisterMatch)
                    {
                        registerMatch++;
                    }
                    else
                    {
                        reslice++;

                        if (!namesAgree)
                        {
                            resliceNameDisagreement++;
                        }
                    }
                }
            }

            await context.SaveChangesAsync(cancellationToken);

            var directoryWithNoCandidate = directory.Count(row => !pairedDirectoryRows.Contains(row.PlaceId));

            MatcherLog.Completed(
                logger,
                units.Count,
                directory.Count,
                registerMatch,
                reslice,
                resliceNameDisagreement,
                unitsWithNoCandidate,
                directoryWithNoCandidate);

            return Result<ProposalSummary>.Success(new ProposalSummary(
                units.Count,
                directory.Count,
                registerMatch,
                reslice,
                resliceNameDisagreement,
                alreadyProposed,
                unitsWithNoCandidate,
                directoryWithNoCandidate));
        }

        /// <summary>
        /// Re-slices a ten-digit canonical code into the nine-digit historical form.
        /// </summary>
        /// <remarks>
        /// The documented relationship for most provincial units: the ten-digit form inserts a digit
        /// after the region, so <c>0102801000</c> becomes <c>012801000</c>. It is a <em>candidate</em>
        /// generator and nothing more — for Metro Manila it produces a code that does not exist, and for
        /// some units it produces a code belonging to a different place, which is exactly why the
        /// evidence value it yields cannot be confirmed without name agreement.
        /// </remarks>
        private static string? ResliceToNineDigits(string canonicalCode)
        {
            if (canonicalCode.Length != 10)
            {
                return null;
            }

            // Drop the second digit: RR|D|PPMMBBB → RR|PPMMBBB. Region is the leading pair in both
            // editions, and the inserted digit is the one the 2019 edition added.
            var resliced = string.Concat(canonicalCode.AsSpan(0, 1), canonicalCode.AsSpan(2, 8));

            return resliced.Length == 9 ? resliced : null;
        }

        /// <summary>
        /// Whether two published names refer to the same unit, conservatively.
        /// </summary>
        /// <remarks>
        /// Normalises case, diacritics, punctuation, whitespace and the city affixes the two publishers
        /// disagree about — GeoNames abbreviates where the register writes <c>City of</c>, which is the
        /// documented cause of roughly ten cities appearing as municipalities in the directory. No fuzzy
        /// distance: a reviewer must be able to check a proposal at a glance, and an 85%-similar name is
        /// not checkable at a glance.
        /// </remarks>
        internal static bool NamesAgree(string registerName, string directoryName) =>
            string.Equals(Normalise(registerName), Normalise(directoryName), StringComparison.Ordinal);

        private static string Normalise(string value)
        {
            var folded = value
                .Normalize(NormalizationForm.FormD)
                .Where(character => CharUnicodeInfo.GetUnicodeCategory(character)
                    != UnicodeCategory.NonSpacingMark)
                .ToArray();

            var text = new string(folded)
                .ToLowerInvariant()
                .Replace("city of ", string.Empty, StringComparison.Ordinal)
                .Replace("municipality of ", string.Empty, StringComparison.Ordinal)
                .Replace("province of ", string.Empty, StringComparison.Ordinal);

            var kept = text
                .Where(character => char.IsAsciiLetterOrDigit(character))
                .ToArray();

            var stripped = new string(kept);

            // The trailing "city" marker is dropped last, after punctuation, so "Cebu City" and
            // "City of Cebu" and "Cebu" all normalise alike. Dropping it earlier would also strip the
            // "city" inside a name that genuinely contains it.
            return stripped.EndsWith("city", StringComparison.Ordinal) && stripped.Length > 4
                ? stripped[..^4]
                : stripped;
        }

        private sealed record DirectoryRow(Guid PlaceId, string PsgcCode, string Name, PlaceKind Kind);
    }
}

internal static partial class MatcherLog
{
    [LoggerMessage(
        EventId = 7110,
        Level = LogLevel.Information,
        Message = "LGU matcher proposed pairings for {RegisterUnits} register units against "
            + "{DirectoryRows} directory rows: {RegisterMatch} on the register's own pairing, "
            + "{Reslice} on digit re-slicing ({NameDisagreement} of which have disagreeing names and "
            + "cannot be confirmed on that evidence). {UnitsWithNoCandidate} units and "
            + "{DirectoryWithNoCandidate} directory rows had no candidate at all")]
    public static partial void Completed(
        ILogger logger,
        int registerUnits,
        int directoryRows,
        int registerMatch,
        int reslice,
        int nameDisagreement,
        int unitsWithNoCandidate,
        int directoryWithNoCandidate);

    [LoggerMessage(
        EventId = 7111,
        Level = LogLevel.Error,
        Message = "Matcher produced a proposal the domain refused for {CanonicalCode} → "
            + "{HistoricalCode}: {ErrorCode}. This is a matcher defect, not a register property.")]
    public static partial void ProposalRefused(
        ILogger logger,
        string canonicalCode,
        string historicalCode,
        string errorCode);
}
