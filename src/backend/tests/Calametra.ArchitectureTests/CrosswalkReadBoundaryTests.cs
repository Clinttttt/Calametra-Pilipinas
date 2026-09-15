using System.Reflection;
using Calametra.Application.Abstractions.Data;
using Calametra.Domain.Administrative;
using Shouldly;

namespace Calametra.ArchitectureTests;

/// <summary>
/// Layer 3 of the crosswalk read boundary: the tests that stop it being reintroduced.
/// </summary>
/// <remarks>
/// <para>
/// ADR-005 D4 states three layers that fail independently — a <c>Confirmed</c>-only view, database
/// permissions where the deployment allows separate roles, and these tests. What is asserted here is
/// structural rather than behavioural, because that is what survives refactoring: an unreviewed pairing
/// must be <em>unreachable</em> from the surface analytics hold, not merely unqueried by today's handlers.
/// </para>
/// <para>
/// The failure these prevent is a plausible one. A handler that needs a code pairing, finding none on
/// <see cref="IApplicationDbContext"/>, could reasonably be "fixed" by adding the base table to that
/// interface. It would compile, pass every behavioural test, and silently let proposals into figures
/// rendered to readers.
/// </para>
/// </remarks>
public sealed class CrosswalkReadBoundaryTests
{
    [Fact]
    public void The_analytics_surface_does_not_expose_the_crosswalk_base_table()
    {
        var exposed = typeof(IApplicationDbContext)
            .GetProperties()
            .Select(property => property.PropertyType)
            .Where(type => type.IsGenericType)
            .SelectMany(type => type.GetGenericArguments())
            .ToList();

        exposed.ShouldNotContain(typeof(LguCodeLink));
    }

    [Fact]
    public void The_analytics_surface_exposes_the_confirmed_only_read_model()
    {
        var exposed = typeof(IApplicationDbContext)
            .GetProperties()
            .Select(property => property.PropertyType)
            .Where(type => type.IsGenericType)
            .SelectMany(type => type.GetGenericArguments())
            .ToList();

        exposed.ShouldContain(typeof(ConfirmedLguLink));
    }

    [Fact]
    public void The_confirmed_only_read_model_cannot_be_mutated_by_a_caller()
    {
        // Read-only by construction as well as by mapping: a settable property would let a handler hand a
        // modified pairing to something that renders it, which is a different failure from writing to the
        // database but no less misleading.
        var settable = typeof(ConfirmedLguLink)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is { IsPublic: true })
            .Select(property => property.Name)
            .ToList();

        settable.ShouldBeEmpty();
    }

    [Fact]
    public void Only_the_administrative_feature_may_take_the_review_surface()
    {
        // The whole point of two interfaces. A query handler elsewhere that asked for the review surface
        // would have reintroduced the ability to derive a figure from a proposal, so the namespace is the
        // boundary and this test is what holds it.
        var offenders = Assemblies.Application
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Where(parameter => parameter.ParameterType == typeof(ILguCrosswalkReviewContext))
                .Select(_ => type))
            .Distinct()
            .Where(type => type.FullName?.Contains(
                "Features.Administrative",
                StringComparison.Ordinal) != true)
            .Select(type => type.FullName!)
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void The_review_surface_is_the_only_place_the_base_table_is_reachable()
    {
        var exposed = typeof(ILguCrosswalkReviewContext)
            .GetProperties()
            .Select(property => property.PropertyType)
            .Where(type => type.IsGenericType)
            .SelectMany(type => type.GetGenericArguments())
            .ToList();

        exposed.ShouldContain(typeof(LguCodeLink));
    }
}
