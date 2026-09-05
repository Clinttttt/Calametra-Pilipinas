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
/// between the service and abuse, which makes it load-bearing rather than
/// decorative. <see cref="HazardProxy"/> is deliberately much tighter than
/// <see cref="PublicRead"/>: those requests are forwarded to a government map
/// service, so a client hammering Calametra would otherwise become Calametra
/// hammering PHIVOLCS.
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
