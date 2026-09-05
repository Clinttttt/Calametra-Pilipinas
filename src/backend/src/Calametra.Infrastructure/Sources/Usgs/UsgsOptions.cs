using System.ComponentModel.DataAnnotations;

namespace Calametra.Infrastructure.Sources.Usgs;

/// <summary>Configuration for the USGS FDSN event service adapter.</summary>
public sealed class UsgsOptions
{
    public const string SectionName = "Sources:Usgs";

    /// <summary>Slug of the corresponding <c>DataSource</c> row.</summary>
    public const string Slug = "usgs-comcat";

    [Required]
    public Uri BaseAddress { get; set; } = new("https://earthquake.usgs.gov/");

    /// <summary>FDSN event query path.</summary>
    public string QueryPath { get; set; } = "fdsnws/event/1/query";

    /// <summary>
    /// Per-request timeout. Generous because a multi-year backfill window can return
    /// several thousand features in one response.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Contact string sent as User-Agent. The USGS asks that automated clients
    /// identify themselves so they can reach the operator if a client misbehaves.
    /// </summary>
    public string UserAgent { get; set; } = "Calametra-CARAGA/0.1 (academic research platform)";
}
