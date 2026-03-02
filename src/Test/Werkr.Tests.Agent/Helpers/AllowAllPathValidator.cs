using Werkr.Core.Security;

namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// Fake <see cref="IPathAllowlistValidator"/> that permits every path.
/// Used by handler unit tests where path security is not under test.
/// </summary>
internal sealed class AllowAllPathValidator : IPathAllowlistValidator {

    public void ValidatePath( string path ) { }

    public void ValidatePaths( params string[] paths ) { }

    public bool IsPathAllowed( string path ) => true;
}
