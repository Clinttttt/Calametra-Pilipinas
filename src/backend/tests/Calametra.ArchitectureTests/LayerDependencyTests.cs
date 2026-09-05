using NetArchTest.Rules;

namespace Calametra.ArchitectureTests;

/// <summary>
/// Rules the compiler cannot enforce.
/// </summary>
/// <remarks>
/// Project references already make several classic Clean Architecture tests
/// unnecessary — Domain cannot reference Application because the .csproj says so.
/// These are the rules that survive that: package-level constraints, visibility,
/// naming, and the one honest exception the architecture makes.
/// </remarks>
public sealed class LayerDependencyTests
{
    [Fact]
    public void Domain_ShouldHaveNoProjectReferences()
    {
        var referenced = Assemblies.Domain
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .Where(name => name is not null && name.StartsWith("Calametra", StringComparison.Ordinal))
            .ToArray();

        referenced.ShouldBeEmpty(
            "Calametra.Domain must reference no other Calametra project. It is the innermost layer.");
    }

    [Fact]
    public void Domain_ShouldNotDependOnAnyFramework()
    {
        // NetTopologySuite is the single permitted package. See ADR-002: it is a pure
        // geometry value-type library with no I/O, and this application's domain is
        // inherently geospatial. Everything else listed here would be a real violation.
        var forbidden = new[]
        {
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore",
            "Microsoft.Extensions",
            "Npgsql",
            "Serilog",
            "FluentValidation",
            "System.Net.Http",
        };

        Types.InAssembly(Assemblies.Domain)
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue("Calametra.Domain must contain no framework or infrastructure dependency.");
    }

    [Fact]
    public void Domain_ShouldOnlyReferenceNetTopologySuite()
    {
        var nonSystemPackages = Assemblies.Domain
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name!)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal)
                && !name.Equals("netstandard", StringComparison.Ordinal)
                && !name.Equals("mscorlib", StringComparison.Ordinal))
            .ToArray();

        // If this fails, a package crept into Domain. Either remove it or record a
        // new ADR explaining why it belongs there — do not simply widen this list.
        nonSystemPackages.ShouldBe(
            ["NetTopologySuite"],
            ignoreOrder: true,
            customMessage: "Calametra.Domain permits exactly one package: NetTopologySuite (ADR-002).");
    }

    [Fact]
    public void Application_ShouldNotDependOnInfrastructureOrProvider()
    {
        // Application takes a deliberate EF Core reference so handlers keep LINQ
        // composition, but it must never learn WHICH database is behind it.
        Types.InAssembly(Assemblies.Application)
            .ShouldNot()
            .HaveDependencyOnAny("Npgsql", "Calametra.Infrastructure", "Calametra.Api")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue(
                "Calametra.Application may reference EF Core abstractions but never a provider or an outer layer.");
    }

    [Fact]
    public void Application_ShouldNotDependOnAspNetCore()
    {
        // This is what makes HttpContext, IFormFile and IResult unwritable in a
        // handler rather than merely discouraged.
        Types.InAssembly(Assemblies.Application)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue("Requests and handlers must stay transport-agnostic.");
    }

    [Fact]
    public void Infrastructure_ShouldNotDependOnApi()
    {
        Types.InAssembly(Assemblies.Infrastructure)
            .ShouldNot()
            .HaveDependencyOn("Calametra.Api")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue("Adapters must not know about the HTTP host.");
    }
}
