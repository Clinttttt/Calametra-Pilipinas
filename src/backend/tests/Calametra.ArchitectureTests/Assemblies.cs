using System.Reflection;

namespace Calametra.ArchitectureTests;

/// <summary>Assembly handles, resolved once.</summary>
internal static class Assemblies
{
    public static readonly Assembly Domain = typeof(Domain.Abstractions.Result).Assembly;

    public static readonly Assembly Application =
        typeof(Application.DependencyInjection).Assembly;

    public static readonly Assembly Infrastructure =
        typeof(Infrastructure.DependencyInjection).Assembly;

    public static readonly Assembly Api = typeof(Program).Assembly;
}
