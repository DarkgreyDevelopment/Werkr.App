using Werkr.Agent.Security;
using Werkr.Core.Security;

namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// Creates an <see cref="IFilePathResolver"/> backed by <see cref="AllowAllPathValidator"/>
/// for handler tests that need path resolution but not enforcement.
/// </summary>
internal static class TestFilePathResolver {

    /// <summary>Gets a resolver that allows all paths.</summary>
    public static IFilePathResolver AllowAll { get; } = new FilePathResolver( new AllowAllPathValidator( ) );

    /// <summary>Gets a resolver that denies all paths.</summary>
    public static IFilePathResolver DenyAll { get; } = new FilePathResolver( new DenyAllPathValidator( ) );

    /// <summary>Creates a resolver that only allows paths under the given prefixes.</summary>
    public static IFilePathResolver AllowPrefixes( params string[] prefixes ) =>
        new FilePathResolver( new AllowPrefixValidator( prefixes ) );
}
