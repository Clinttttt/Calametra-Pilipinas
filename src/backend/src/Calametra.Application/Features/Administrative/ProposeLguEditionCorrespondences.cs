using System.Globalization;
using System.Text;

using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Abstractions.Sources;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Administrative;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Calametra.Application.Features.Administrative;

/// <summary>
/// Proposes correspondences from an external dataset's legacy ten-digit codes to current units.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two bridges, both ending in the confirmed crosswalk.</b> Neither is a prefix rule, and the difference
/// matters: the recodings are regular enough that a rule looks safe, and a rule would have attached the City
/// of Manila's outline to Tondo, because the boundary set codes Manila <c>1303901000</c> whose re-slice is a
/// district code this platform holds as an accepted sub-city exception.
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Register bridge.</b> The legacy code is a unit this platform itself held under an earlier PSA
/// edition. That superseded row carries the PSA's own stated nine-digit code, and a <em>confirmed</em>
/// pairing resolves it to a current unit. This is the platform's own record of the earlier register.
/// </item>
/// <item>
/// <b>Re-slice bridge.</b> The ten-digit code re-sliced to its nine-digit sibling by the documented rule,
/// then resolved through the same confirmed crosswalk. The arithmetic only finds the candidate; the
/// reviewed pairing is what could make it true.
/// </item>
/// </list>
/// <para>
/// Where the bridges disagree the row is still proposed, but with both targets named in its basis and
/// <c>NamesAgree</c> false, so the domain refuses to confirm it as
/// <see cref="LguLinkEvidence.EditionCorrespondence"/> and a person must decide. Nothing is dropped
/// silently and nothing is resolved by preferring one bridge.
/// </para>
/// </remarks>
public static class ProposeLguEditionCorrespondences
{
    public const string MatcherName = "calametra.edition-correspondence/1.0";

    public sealed record Command : ICommand<CorrespondenceProposalSummary>;

    /// <param name="AlreadyCurrent">
    /// Legacy codes the active edition still uses. No correspondence is needed and none is written.
    /// </param>
    /// <param name="Unreachable">
    /// Legacy codes from which no confirmed nine-digit pairing can be reached by either bridge. Reported
    /// rather than guessed at — these are the units that will need a written D5 exception or an operator
    /// document.
    /// </param>
    public sealed record CorrespondenceProposalSummary(
        string EditionLabel,
        int LegacyCodesExamined,
        int AlreadyCurrent,
        int Proposed,
        int AlreadyHeld,
        int NamesAgree,
        int NamesDisagree,
        int BridgesDisagree,
        int RegisterBridge,
        int ResliceOnly,
        IReadOnlyList<string> Unreachable);

    internal sealed class Handler(
        ILguCrosswalkReviewContext review,
        IBoundaryCatalogueSource catalogue,
        TimeProvider timeProvider,
        ILogger<Handler> logger) : ICommandHandler<Command, CorrespondenceProposalSummary>
    {
        private static readonly Error NoCurrentEdition = new(
            ErrorType.Validation,
            "edition_correspondence.no_current_edition",
            "No current PSGC register edition is held, so there is nothing to correspond to.");

        public async Task<Result<CorrespondenceProposalSummary>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            var now = timeProvider.GetUtcNow();

            var edition = await review.PsgcRegisterEditions
                .Where(candidate => candidate.SupersededAt == null)
                .OrderByDescending(candidate => candidate.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (edition is null)
            {
                return Result<CorrespondenceProposalSummary>.Failure(NoCurrentEdition);
            }

            var units = await review.Lgus
                .Select(lgu => new
                {
                    lgu.Id,
                    lgu.CanonicalPsgcCode,
                    lgu.Name,
                    lgu.RegisterEditionId,
                    lgu.RegisterStatedHistoricalCode,
                })
                .ToListAsync(cancellationToken);

            var currentByCode = units
                .Where(unit => unit.RegisterEditionId == edition.Id)
                .ToDictionary(unit => unit.CanonicalPsgcCode, StringComparer.Ordinal);

            // Units held under an earlier edition, keyed by the code that edition used. This is the register
            // bridge's evidence: the platform's own reading of the previous publication.
            var supersededByCode = units
                .Where(unit => unit.RegisterEditionId != edition.Id)
                .GroupBy(unit => unit.CanonicalPsgcCode, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            // The reviewed pairings only. Reading proposals here would let an unconfirmed guess decide which
            // unit a legacy code means, which is the substitution D4 exists to prevent.
            var confirmed = await review.LguCodeLinks
                .Where(link => link.Status == LguLinkStatus.Confirmed)
                .Select(link => new { link.LguId, link.HistoricalPsgcCode })
                .ToListAsync(cancellationToken);

            var unitById = units.ToDictionary(unit => unit.Id);

            var currentByHistoricalCode = new Dictionary<string, Guid>(StringComparer.Ordinal);

            foreach (var link in confirmed)
            {
                if (unitById.TryGetValue(link.LguId, out var unit)
                    && unit.RegisterEditionId == edition.Id)
                {
                    currentByHistoricalCode.TryAdd(link.HistoricalPsgcCode, link.LguId);
                }
            }

            var existing = await review.LguEditionCorrespondences
                .Where(item => item.Status != LguLinkStatus.Superseded)
                .Select(item => item.LegacyCanonicalPsgcCode)
                .ToListAsync(cancellationToken);

            var alreadyHeldCodes = existing.ToHashSet(StringComparer.Ordinal);

            var legacyUnits = await catalogue.ReadUnitsAsync(cancellationToken);

            var alreadyCurrent = 0;
            var proposed = 0;
            var alreadyHeld = 0;
            var namesAgree = 0;
            var namesDisagree = 0;
            var bridgesDisagree = 0;
            var registerBridge = 0;
            var resliceOnly = 0;
            var unreachable = new List<string>();

            foreach (var legacy in legacyUnits)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var code = legacy.CanonicalCode;

                if (currentByCode.ContainsKey(code))
                {
                    alreadyCurrent++;

                    continue;
                }

                if (alreadyHeldCodes.Contains(code))
                {
                    alreadyHeld++;

                    continue;
                }

                Guid? viaRegister = null;
                string? registerHistorical = null;

                if (supersededByCode.TryGetValue(code, out var held)
                    && !string.IsNullOrEmpty(held.RegisterStatedHistoricalCode)
                    && currentByHistoricalCode.TryGetValue(
                        held.RegisterStatedHistoricalCode,
                        out var fromRegister))
                {
                    viaRegister = fromRegister;
                    registerHistorical = held.RegisterStatedHistoricalCode;
                }

                Guid? viaReslice = null;
                var resliced = Reslice(code);

                if (resliced is not null
                    && currentByHistoricalCode.TryGetValue(resliced, out var fromReslice))
                {
                    viaReslice = fromReslice;
                }

                if (viaRegister is null && viaReslice is null)
                {
                    unreachable.Add(
                        $"{code} '{legacy.Name}': no confirmed nine-digit pairing reachable by either bridge");

                    continue;
                }

                var disagree = viaRegister is not null
                    && viaReslice is not null
                    && viaRegister != viaReslice;

                // The register bridge is preferred as the proposal's target because it rests on a
                // publication this platform read, not on arithmetic. Where they disagree that preference
                // decides nothing: the row is marked as disagreeing and the domain will refuse to confirm it
                // mechanically.
                var targetId = viaRegister ?? viaReslice!.Value;
                var target = unitById[targetId];

                var agree = !disagree && NamesMatch(legacy.Name, target.Name);

                var basis = BuildBasis(
                    code,
                    resliced,
                    registerHistorical,
                    viaRegister is null ? null : unitById[viaRegister.Value].CanonicalPsgcCode,
                    viaReslice is null ? null : unitById[viaReslice.Value].CanonicalPsgcCode,
                    disagree);

                var candidate = LguEditionCorrespondence.Propose(
                    code,
                    legacy.Name,
                    targetId,
                    target.CanonicalPsgcCode,
                    basis,
                    agree,
                    viaRegister is not null && !disagree,
                    MatcherName,
                    edition.Id,
                    now);

                if (candidate.IsFailure)
                {
                    unreachable.Add($"{code} '{legacy.Name}': {candidate.Error!.Code}");

                    continue;
                }

                review.LguEditionCorrespondences.Add(candidate.Value);

                proposed++;

                if (agree)
                {
                    namesAgree++;
                }
                else
                {
                    namesDisagree++;
                }

                if (disagree)
                {
                    bridgesDisagree++;
                }
                else if (viaRegister is not null)
                {
                    registerBridge++;
                }
                else
                {
                    resliceOnly++;
                }
            }

            await review.SaveChangesAsync(cancellationToken);

            CorrespondenceLog.Proposed(
                logger,
                proposed,
                namesAgree,
                namesDisagree,
                bridgesDisagree,
                unreachable.Count);

            return Result<CorrespondenceProposalSummary>.Success(new CorrespondenceProposalSummary(
                edition.Label,
                legacyUnits.Count,
                alreadyCurrent,
                proposed,
                alreadyHeld,
                namesAgree,
                namesDisagree,
                bridgesDisagree,
                registerBridge,
                resliceOnly,
                unreachable));
        }

        /// <summary>
        /// The documented ten-to-nine re-slicing: the ten-digit form carries one more digit in its region
        /// pair.
        /// </summary>
        /// <remarks>
        /// Adams is <c>0102801000</c> against <c>012801000</c>, which is the example ADR-005 itself uses.
        /// This produces a <em>candidate key</em> and nothing more — what makes a correspondence true is the
        /// confirmed pairing the key lands on, plus the names agreeing.
        /// </remarks>
        private static string? Reslice(string canonicalCode) =>
            canonicalCode.Length == 10 && canonicalCode[2] == '0'
                ? canonicalCode.Remove(2, 1)
                : null;

        private static string BuildBasis(
            string legacyCode,
            string? resliced,
            string? registerHistorical,
            string? viaRegisterCode,
            string? viaResliceCode,
            bool disagree)
        {
            var basis = new StringBuilder();

            basis.Append(CultureInfo.InvariantCulture, $"legacy {legacyCode}");

            if (viaRegisterCode is not null)
            {
                basis.Append(CultureInfo.InvariantCulture,
                    $"; register bridge via superseded unit stating {registerHistorical} -> {viaRegisterCode}");
            }

            if (viaResliceCode is not null)
            {
                basis.Append(CultureInfo.InvariantCulture,
                    $"; reslice bridge via {resliced} -> {viaResliceCode}");
            }

            if (disagree)
            {
                basis.Append(CultureInfo.InvariantCulture,
                    $"; BRIDGES DISAGREE: register says {viaRegisterCode}, reslice says {viaResliceCode}");
            }

            return basis.ToString();
        }

        /// <summary>
        /// Whether two editions call a unit the same thing, allowing for the ways registers write names.
        /// </summary>
        /// <remarks>
        /// Folds case, diacritics and punctuation, strips the "City of" / "City" forms both editions use
        /// interchangeably, and drops parenthesised former names — the boundary set writes "Amlan
        /// (Ayuquitan)" and "Hinoba-an (Asia)" where the register writes the current name alone. This is
        /// corroboration only: agreement never establishes identity here, it merely permits a bridge that
        /// already reached a reviewed pairing to be confirmed mechanically.
        /// </remarks>
        private static bool NamesMatch(string? left, string? right) =>
            !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && string.Equals(Fold(left), Fold(right), StringComparison.Ordinal);

        private static string Fold(string value)
        {
            var withoutParentheticals = System.Text.RegularExpressions.Regex.Replace(
                value,
                @"\([^)]*\)",
                " ");

            var withoutTitles = withoutParentheticals
                .Replace("City of", " ", StringComparison.OrdinalIgnoreCase)
                .Replace("Municipality of", " ", StringComparison.OrdinalIgnoreCase)
                .Replace("City", " ", StringComparison.OrdinalIgnoreCase);

            var decomposed = withoutTitles.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);

            foreach (var character in decomposed)
            {
                if (char.IsAsciiLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                }
            }

            return builder.ToString();
        }
    }
}

internal static partial class CorrespondenceLog
{
    [LoggerMessage(
        EventId = 7350,
        Level = LogLevel.Information,
        Message = "Edition correspondence proposed for {Proposed} legacy code(s): {NamesAgree} with agreeing "
            + "names, {NamesDisagree} without, {BridgesDisagree} where the bridges disagree. {Unreachable} "
            + "code(s) reached no confirmed pairing.")]
    public static partial void Proposed(
        ILogger logger,
        int proposed,
        int namesAgree,
        int namesDisagree,
        int bridgesDisagree,
        int unreachable);

    [LoggerMessage(
        EventId = 7351,
        Level = LogLevel.Information,
        Message = "Edition correspondence class confirmed by {ReviewedBy}: {Confirmed} confirmed, "
            + "{Refused} refused by the domain and left proposed")]
    public static partial void ClassConfirmed(
        ILogger logger,
        string reviewedBy,
        int confirmed,
        int refused);

    [LoggerMessage(
        EventId = 7352,
        Level = LogLevel.Information,
        Message = "Correspondence {LegacyCode} -> {CurrentCode} confirmed on {Evidence} by {ReviewedBy}")]
    public static partial void SingleConfirmed(
        ILogger logger,
        string legacyCode,
        string currentCode,
        string evidence,
        string reviewedBy);
}
