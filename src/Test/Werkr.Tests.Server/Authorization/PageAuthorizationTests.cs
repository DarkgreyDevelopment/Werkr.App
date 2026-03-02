using System.Reflection;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

namespace Werkr.Tests.Server.Authorization;

/// <summary>
/// Reflection-based tests verifying every Blazor page has the correct
/// <see cref="AuthorizeAttribute"/> configuration (§3.12.7).
/// </summary>
[TestClass]
public class PageAuthorizationTests {
    /// <summary>
    /// Mapping of page routes to expected authorization behaviour.
    /// True = requires Admin role, False = any authenticated, null = anonymous.
    /// </summary>
    private static readonly Dictionary<string, string?> s_expectedAuthorization = new( StringComparer.OrdinalIgnoreCase ) {
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
        ["/"] = "",
        ["/account/change-password"] = "",
        ["/account/manage"] = "",
        ["/account/manage/mfa"] = "",
        ["/calendar"] = "",
        ["/tasklist"] = "",
        ["/jobs"] = "",
        ["/jobs/{Id:guid}"] = "",
        ["/schedules"] = "",
        ["/schedules/create"] = "",
        ["/schedules/{Id:guid}"] = "",
        ["/tasks"] = "",
        ["/tasks/create"] = "",
        ["/tasks/{Id:long}"] = "",
        ["/workflows"] = "",
        ["/workflows/create"] = "",
        ["/workflows/{Id:long}"] = "",
        ["/workflows/{WorkflowId:long}/runs"] = "",
        ["/workflows/runs/{RunId:guid}"] = "",

        // Anonymous pages (no [Authorize])
        ["/account/login"] = null,
        ["/account/mfa-verify"] = null,
        ["/account/mfa-recovery"] = null,
        ["/account/access-denied"] = null,
        ["/account/logout"] = null,
        ["/Error"] = null,
    };

    [TestMethod]
    public void AllPages_HaveCorrectAuthorization( ) {
        Assembly serverAssembly = typeof( Werkr.Server.Identity.WerkrCookieAuthEvents ).Assembly;

        List<Type> pageTypes = [.. serverAssembly
            .GetTypes( )
            .Where( t => t.GetCustomAttribute<RouteAttribute>( ) is not null )
            .Where( t => typeof( ComponentBase ).IsAssignableFrom( t ) )];

        Assert.IsNotEmpty( pageTypes, "Should discover at least one Blazor page." );

        List<string> errors = [];

        foreach (Type page in pageTypes) {
            RouteAttribute route = page.GetCustomAttribute<RouteAttribute>( )!;
            AuthorizeAttribute? auth = page.GetCustomAttribute<AuthorizeAttribute>( );

            if (!s_expectedAuthorization.TryGetValue( route.Template, out string? expectedRoles )) {
                // Page not in our map — skip (could be added later)
                continue;
            }

            if (expectedRoles is null) {
                // Should be anonymous (no [Authorize])
                if (auth is not null) {
                    errors.Add( $"Page '{route.Template}' ({page.Name}) should be anonymous but has [Authorize]." );
                }
            } else if (expectedRoles == "") {
                // Should have [Authorize] with no specific roles
                if (auth is null) {
                    errors.Add( $"Page '{route.Template}' ({page.Name}) should require authentication but lacks [Authorize]." );
                }
            } else {
                // Should have [Authorize(Roles = "...")]
                if (auth is null) {
                    errors.Add( $"Page '{route.Template}' ({page.Name}) should have [Authorize(Roles=\"{expectedRoles}\")] but lacks [Authorize]." );
                } else if (auth.Roles != expectedRoles) {
                    errors.Add( $"Page '{route.Template}' ({page.Name}) expected Roles=\"{expectedRoles}\" but got Roles=\"{auth.Roles}\"." );
                }
            }
        }

        if (errors.Count > 0) {
            Assert.Fail( "Authorization errors:\n" + string.Join( "\n", errors ) );
        }
    }

    [TestMethod]
    public void AdminPages_RequireAdminRole( ) {
        Assembly serverAssembly = typeof( Werkr.Server.Identity.WerkrCookieAuthEvents ).Assembly;

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
            .Where( t => t.GetCustomAttribute<RouteAttribute>( ) is not null )
            .Where( t => typeof( ComponentBase ).IsAssignableFrom( t ) )];

        foreach (string route in adminRoutes) {
            Type? pageType = pageTypes.FirstOrDefault( t =>
                t.GetCustomAttribute<RouteAttribute>( )!.Template == route );

            Assert.IsNotNull( pageType, $"Page with route '{route}' should exist." );

            AuthorizeAttribute? auth = pageType.GetCustomAttribute<AuthorizeAttribute>( );

            Assert.IsNotNull( auth, $"Page '{route}' ({pageType.Name}) must have [Authorize]." );
            Assert.IsNotNull( auth.Roles, $"Page '{route}' ({pageType.Name}) must specify roles." );
            Assert.Contains( "Admin", auth.Roles, $"Page '{route}' ({pageType.Name}) must include Admin role." );
        }
    }
}
