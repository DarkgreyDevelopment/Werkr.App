using Microsoft.AspNetCore.Authorization;

namespace Werkr.Tests.Server.Authorization;

/// <summary>
/// Verifies that Blazor pages have the correct <see cref="AuthorizeAttribute"/> configuration.
/// These are reflection-based tests that validate server-side authorization gates independent of NavMenu visibility.
/// </summary>
[TestClass]
public class AuthorizationAttributeTests {
}
