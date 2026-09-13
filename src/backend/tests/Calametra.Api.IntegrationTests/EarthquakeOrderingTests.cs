using System.Net;

namespace Calametra.Api.IntegrationTests;

/// <summary>
/// How the catalogue may be ordered, and the ordering it refuses.
/// </summary>
/// <remarks>
/// <para>
/// The refusal is the point. This archive is 92.8% body-wave readings while nearly every large event
/// is reported as a moment magnitude, so ranking a mixed list by magnitude value would place events in
/// an order that belongs to the scale rather than to the earthquake — the exact comparison
/// <c>MagnitudeType.IsComparableWith</c> refuses everywhere else in the codebase. An API that served
/// it anyway would make itself the one place the rule does not hold.
/// </para>
/// <para>
/// Asserted at the HTTP boundary rather than on the validator, because what matters is that a caller
/// cannot obtain the ranking — not that a particular class rejects it.
/// </para>
/// </remarks>
[Collection(PostgisCollection.Name)]
public sealed class EarthquakeOrderingTests(PostgisApiFixture fixture)
{
    [Fact]
    public async Task RankingByMagnitude_ShouldBeRefusedWithoutAScaleFamily()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync(
            new Uri("/api/earthquakes?sort=Strongest&pageSize=1", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The message has to say why, because the caller's request was reasonable and the reason is a
        // property of the catalogue rather than of the API.
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldContain("scale family");
    }

    [Fact]
    public async Task RankingByMagnitude_ShouldBeAllowedWithinOneScaleFamily()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync(
            new Uri("/api/earthquakes?sort=Strongest&scaleFamily=Moment&pageSize=1", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("Newest")]
    [InlineData("Oldest")]
    public async Task OrderingByTime_ShouldNeedNoQualification(string sort)
    {
        using var client = fixture.CreateClient();

        // Time is the one ordering that is always valid: every reading carries an origin instant, and
        // instants are comparable across agencies and scales in a way magnitudes are not.
        var response = await client.GetAsync(
            new Uri($"/api/earthquakes?sort={sort}&pageSize=1", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
