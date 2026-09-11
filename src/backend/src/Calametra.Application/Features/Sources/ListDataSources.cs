using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Sources;

/// <summary>
/// Every upstream dataset this platform reads, with its licence position and its known limits.
/// </summary>
/// <remarks>
/// <para>
/// The About Data page is generated from this, so the credits a reader sees are the same rows
/// ingestion and rendering actually use. Attribution written into a page by hand drifts from the
/// system the moment a source is added — which had happened: the page listed the three hazard
/// layers and therefore credited GEM and PHIVOLCS, while the USGS catalogue behind every
/// earthquake, the six cyclone archives behind every track, the transcribed PHIVOLCS bulletins
/// and the gazetteer behind every place name appeared nowhere.
/// </para>
/// <para>
/// <b>Coverage notes are returned in full.</b> They are the platform's own statement of what a
/// source does not cover, and truncating them for layout would leave the shortened half sounding
/// more confident than the source is.
/// </para>
/// <para>
/// <b>Ordered with the Philippine authorities first.</b> This is a platform about the
/// Philippines, and a reader looking for who to trust on a Philippine hazard should not have to
/// scan an alphabetical list to find that PHIVOLCS is in it. Everything else follows by agency,
/// so the order is stable rather than a judgement about relative quality.
/// </para>
/// </remarks>
public static class ListDataSources
{
    public sealed record Query : IQuery<IReadOnlyList<DataSourceResponse>>;

    /// <param name="AccessKind">
    /// How the data is obtained — a bulk file, a REST service, or imagery proxied from the
    /// publisher's own map service. Not a licence statement; see
    /// <paramref name="IsRedistributable"/>.
    /// </param>
    /// <param name="IsRedistributable">
    /// Whether Calametra may store this source's data and serve it onward. False means the
    /// platform holds none of it and can only display the publisher's own rendering.
    /// </param>
    /// <param name="IsAuthoritativeForPhilippines">
    /// Whether this is an official Philippine authority for its hazard domain. A reader deciding
    /// whom to act on needs this, and it is not inferable from the agency name.
    /// </param>
    /// <param name="MinimumReliableMagnitude">
    /// Magnitude below which the catalogue is known to be incomplete for the archipelago, where
    /// the concept applies.
    /// </param>
    /// <param name="LastRetrievedAt">
    /// When the platform last read from this source. Null means never — reference data is seeded
    /// before anything is ingested, so a source can be registered and not yet read.
    /// </param>
    public sealed record DataSourceResponse(
        string Slug,
        string Agency,
        string DatasetName,
        string AccessKind,
        string Attribution,
        string? SourceUrl,
        string? TermsUrl,
        bool IsRedistributable,
        bool IsAuthoritativeForPhilippines,
        double? MinimumReliableMagnitude,
        string? CoverageNotes,
        DateTimeOffset? LastRetrievedAt);

    internal sealed class Handler(IApplicationDbContext context)
        : IQueryHandler<Query, IReadOnlyList<DataSourceResponse>>
    {
        public async Task<Result<IReadOnlyList<DataSourceResponse>>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var sources = await context.DataSources.AsNoTracking()
                .OrderByDescending(source => source.IsAuthoritativeForPhilippines)
                .ThenBy(source => source.Agency)
                .ThenBy(source => source.DatasetName)
                .Select(source => new DataSourceResponse(
                    source.Slug,
                    source.Agency,
                    source.DatasetName,
                    source.AccessKind.ToString(),
                    source.Attribution,
                    source.SourceUrl,
                    source.TermsUrl,
                    source.IsRedistributable,
                    source.IsAuthoritativeForPhilippines,
                    source.MinimumReliableMagnitude,
                    source.CoverageNotes,
                    source.LastRetrievedAt))
                .ToListAsync(cancellationToken);

            return Result<IReadOnlyList<DataSourceResponse>>.Success(sources);
        }
    }
}
