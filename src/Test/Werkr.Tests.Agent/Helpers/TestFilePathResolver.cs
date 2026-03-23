using Werkr.Agent.Security;
using Werkr.Core.Security;

namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// Provides pre-built <see cref="IFilePathResolver"/> instances for tests:
/// <see cref="AllowAll"/> (backed by <see cref="AllowAllPathValidator"/>) and
/// <see cref="DenyAll"/> (backed by <see cref="DenyAllPathValidator"/>).
/// </summary>
internal static class TestFilePathResolver {

    /// <summary>Gets a resolver that allows all paths.</summary>
    public static IFilePathResolver AllowAll { get; } = new FilePathResolver( new AllowAllPathValidator( ) );

    /// <summary>Gets a resolver that denies all paths.</summary>
    public static IFilePathResolver DenyAll { get; } = new FilePathResolver( new DenyAllPathValidator( ) );

}
