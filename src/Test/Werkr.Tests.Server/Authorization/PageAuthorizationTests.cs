using System.Reflection;

namespace Werkr.Tests.Server.Authorization;

/// <summary>
/// Reflection-based tests verifying every Blazor page has the correct
/// <see cref="AuthorizeAttribute"/> configuration.
/// </summary>
[TestClass]
public class PageAuthorizationTests {
    /// <summary>
    /// Mapping of page routes to expected authorization behaviour.
    /// True = requires Admin role, False = any authenticated, null = anonymous.
    /// </summary>
    private static readonly Dictionary<string, string?> s_expectedAuthorization =
        new( StringComparer.OrdinalIgnoreCase ) {
            // Admin-only pages
            ["/settings"] = "Admin",
            ["/agents/{AgentId:guid}"] = "Admin",
            ["/agents"] = "Admin",
            ["/admin/users"] = "Admin",
            ["/admin/users/create"] = "Admin",
            ["/admin/users/{UserId}"] = "Admin",
            ["/agents/register"] = "Admin",

            // Admin + Operator pages
            ["/operators"] = "Admin,Operator",
            ["/operators/{AgentId:guid}"] = "Admin,Operator",

            // Any authenticated
            ["/"] = string.Empty,
            ["/account/change-password"] = string.Empty,
            ["/account/manage"] = string.Empty,
            ["/account/manage/mfa"] = string.Empty,
            ["/calendar"] = string.Empty,
            ["/tasklist"] = string.Empty,
            ["/jobs"] = string.Empty,
            ["/jobs/{Id:guid}"] = string.Empty,
            ["/schedules"] = string.Empty,
            ["/schedules/create"] = string.Empty,
            ["/schedules/{Id:guid}"] = string.Empty,
            ["/tasks"] = string.Empty,
            ["/tasks/create"] = string.Empty,
            ["/tasks/{Id:long}"] = string.Empty,
            ["/workflows"] = string.Empty,
            ["/workflows/create"] = string.Empty,
            ["/workflows/{Id:long}"] = string.Empty,
            ["/workflows/{WorkflowId:long}/runs"] = string.Empty,
            ["/workflows/runs/{RunId:guid}"] = string.Empty,

            // Anonymous pages (no [Authorize])
            ["/account/login"] = null,
            ["/account/mfa-verify"] = null,
            ["/account/mfa-recovery"] = null,
            ["/account/access-denied"] = null,
            ["/account/logout"] = null,
            ["/Error"] = null,
        };

    /// <summary>
    /// Scans all Blazor pages in the <c>Werkr.Server</c> assembly via reflection
    /// and verifies that each page's <see cref="AuthorizeAttribute"/> (or lack
    /// thereof) matches the expected authorization policy defined in
    /// <see cref="s_expectedAuthorization"/>. Collects all mismatches and reports
    /// them as a single aggregated failure message.
    /// </summary>
    [TestMethod]
    public void AllPages_HaveCorrectAuthorization( ) {
        Assembly serverAssembly =
            typeof( Werkr.Server.Identity.WerkrCookieAuthEvents ).Assembly;

        List<Type> pageTypes = [.. serverAssembly
            .GetTypes( )
            .Where( t => t.GetCustomAttributes<RouteAttribute>( ).Any( ) )
            .Where( t => typeof( ComponentBase ).IsAssignableFrom( t ) )];

        Assert.IsNotEmpty(
            pageTypes,
            "Should discover at least one Blazor page."
        );

        List<string> errors = [];

        foreach (Type page in pageTypes) {
            IEnumerable<RouteAttribute> routes =
                page.GetCustomAttributes<RouteAttribute>();
            AuthorizeAttribute? auth = page.GetCustomAttribute<AuthorizeAttribute>( );

            foreach (RouteAttribute route in routes) {
                if (!s_expectedAuthorization.TryGetValue( route.Template, out string? expectedRoles )) {
                    continue;
                }

                if (expectedRoles is null) {
                    // Should be anonymous (no [Authorize])
                    if (auth is not null) {
                        errors.Add( $"Page '{route.Template}' ({page.Name}) should be anonymous but has [Authorize]." );
                    }
                } else if (expectedRoles == string.Empty) {
                    // Should have [Authorize] with no specific roles
                    if (auth is null) {
                        errors.Add(
                            $"Page '{route.Template}' ({page.Name}) " +
                            "should require authentication but lacks [Authorize]."
                        );
                    }
                } else {
                    // Should have [Authorize(Roles = "...")]
                    if (auth is null) {
                        errors.Add(
                            $"Page '{route.Template}' ({page.Name}) should have " +
                            $"[Authorize(Roles=\"{expectedRoles}\")] but lacks [Authorize]."
                        );
                    } else if (auth.Roles != expectedRoles) {
                        errors.Add(
                            $"Page '{route.Template}' ({page.Name}) " +
                            $"expected Roles=\"{expectedRoles}\" but got Roles=\"{auth.Roles}\"."
                        );
                    }
                }
            }
        }

        if (errors.Count > 0) {
            Assert.Fail(
                "Authorization errors:\n" + string.Join( "\n", errors )
            );
        }
    }

    /// <summary>
    /// Verifies that all admin-restricted Blazor page routes (such as
    /// "/agents", "/settings", and "/admin/users") have an
    /// <c>[Authorize]</c> attribute whose <c>Roles</c> property includes
    /// "Admin". Uses reflection to discover page types from the
    /// <c>Werkr.Server</c> assembly.
    /// </summary>
    [TestMethod]
    public void AdminPages_RequireAdminRole( ) {
        Assembly serverAssembly =
            typeof( Werkr.Server.Identity.WerkrCookieAuthEvents ).Assembly;

        string[] adminRoutes = [
            "/agents",
            "/agents/{AgentId:guid}",
            "/agents/register",
            "/admin/users",
            "/admin/users/create",
            "/admin/users/{UserId}",
            "/settings"
        ];

        List<Type> pageTypes = [.. serverAssembly
            .GetTypes( )
            .Where( t => t.GetCustomAttributes<RouteAttribute>( ).Any( ) )
            .Where( t => typeof( ComponentBase ).IsAssignableFrom( t ) )];

        foreach (string route in adminRoutes) {
            Type? pageType = pageTypes.FirstOrDefault( t =>
                t.GetCustomAttributes<RouteAttribute>()
                    .Any( r => r.Template == route )
            );

            Assert.IsNotNull(
                pageType,
                $"Page with route '{route}' should exist."
            );

            AuthorizeAttribute? auth =
                pageType.GetCustomAttribute<AuthorizeAttribute>( );

            Assert.IsNotNull(
                auth,
                $"Page '{route}' ({pageType.Name}) must have [Authorize]."
            );
            Assert.IsNotNull(
                auth.Roles,
                $"Page '{route}' ({pageType.Name}) must specify roles."
            );
            Assert.Contains(
                "Admin",
                auth.Roles,
                $"Page '{route}' ({pageType.Name}) must include Admin role."
            );
        }
    }
}
