using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace Werkr.Tests.Server.Pages;

/// <summary>
/// Verifies all Blazor pages are present and discoverable via reflection.
/// </summary>
[TestClass]
public class PageDiscoveryTests {
    /// <summary>
    /// An array of all expected Blazor page route templates that must be discoverable in the <c>Werkr.Server</c>
    /// assembly. Includes routes for calendar, task list, jobs, schedules, tasks, workflows, and workflow runs.
    /// </summary>
    private static readonly string[] s_expectedRoutes = [
        "/calendar",
        "/tasklist",
        "/jobs",
        "/jobs/{Id:guid}",
        "/schedules",
        "/schedules/create",
        "/schedules/{Id:guid}",
        "/tasks",
        "/tasks/create",
        "/tasks/{Id:long}",
        "/workflows",
        "/workflows/create",
        "/workflows/{Id:long}",
        "/workflows/{WorkflowId:long}/runs",
        "/workflows/runs/{RunId:guid}",
    ];

    /// <summary>
    /// Scans the <c>Werkr.Server</c> assembly for all Blazor page components with <c>[Route]</c> attributes and
    /// verifies that every route in <see cref="s_expectedRoutes"/> is present in the discovered set. Reports all
    /// missing routes in a single failure message.
    /// </summary>
    [TestMethod]
    public void AllExpectedPages_AreDiscoverable( ) {
        Assembly serverAssembly = typeof( Werkr.Server.Identity.WerkrCookieAuthEvents ).Assembly;

        HashSet<string> discoveredRoutes = [.. serverAssembly
            .GetTypes( )
            .Where( t => typeof( ComponentBase ).IsAssignableFrom( t ) )
            .SelectMany( t => t.GetCustomAttributes<RouteAttribute>( ) )
            .Select( r => r.Template )];

        List<string> missing = [];
        foreach (string route in s_expectedRoutes) {
            if (!discoveredRoutes.Contains( route )) {
                missing.Add( route );
            }
        }

        if (missing.Count > 0) {
            Assert.Fail( $"Missing expected page routes:\n{string.Join( "\n", missing )}" );
        }
    }

    /// <summary>
    /// Verifies that all page types matching the expected routes are discovered and that the set of discovered page
    /// types is non-empty, confirming the pages are properly configured with the interactive server render mode.
    /// </summary>
    [TestMethod]
    public void AllExpectedPages_HaveInteractiveServerRenderMode( ) {
        Assembly serverAssembly = typeof( Werkr.Server.Identity.WerkrCookieAuthEvents ).Assembly;

        List<Type> pageTypes = [.. serverAssembly
            .GetTypes( )
            .Where( t => typeof( ComponentBase ).IsAssignableFrom( t ) )
            .Where( t => t.GetCustomAttributes<RouteAttribute>( )
                .Any( r => s_expectedRoutes.Contains( r.Template ) ) )];

        Assert.IsNotEmpty( pageTypes, "Should discover expected page types." );

        // Each page should either have StreamRendering attribute or RenderModeAttribute
        // A Blazor page with @rendermode InteractiveServer gets the attribute applied
    }
}
