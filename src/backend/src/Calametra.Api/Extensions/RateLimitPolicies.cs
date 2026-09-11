namespace Calametra.Api.Extensions;

/// <summary>
/// Named rate-limiting policies.
/// </summary>
/// <remarks>
/// <para>
/// <b>Security note, stated explicitly.</b> Calametra's read API is intentionally
/// anonymous: the platform's purpose is public access to hazard information, and
/// requiring accounts to look at earthquake history would defeat it. There is
/// therefore no authentication on these endpoints by design, not by omission.
/// </para>
/// <para>
/// Because there is no authentication, rate limiting is the only thing standing
/// between the service and abuse. But it protects <i>this</i> service, not upstream
/// ones — that distinction was learned by getting it wrong. Throttling tile requests
/// to protect PHIVOLCS returned HTTP 429 to our own map without reducing upstream
/// load, because every request that did get through still reached them. Upstream
/// volume is controlled by caching in <c>PhivolcsHazardMapService</c>; these limits
/// exist to stop a pathological client and are set above legitimate use.
/// </para>
/// <para>
/// Any future write endpoint — curated story content, administrative data
/// corrections — must be authenticated and must not use these policies.
/// </para>
/// </remarks>
internal static class RateLimitPolicies
{
    /// <summary>Anonymous read access to Calametra's own data.</summary>
    public const string PublicRead = "public-read";

    /// <summary>Requests forwarded to an upstream agency map service.</summary>
    public const string HazardProxy = "hazard-proxy";
}
