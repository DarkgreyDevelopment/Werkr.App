using Werkr.Core.Security;

namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// Fake <see cref="IPathAllowlistValidator"/> that permits every path.
/// Used by handler unit tests where path security is not under test.
/// </summary>
internal sealed class AllowAllPathValidator : IPathAllowlistValidator {

    /// <summary>
    /// Validates the specified path. This implementation is a no-op and always succeeds.
    /// </summary>
    public void ValidatePath( string path ) { }

    /// <summary>
    /// Validates multiple paths. This implementation is a no-op and always succeeds.
    /// </summary>
    public void ValidatePaths( params string[] paths ) { }

    /// <summary>
    /// Determines whether the given path is allowed. This implementation always returns <see langword="true"/>.
    /// </summary>
    public bool IsPathAllowed( string path ) => true;
}
