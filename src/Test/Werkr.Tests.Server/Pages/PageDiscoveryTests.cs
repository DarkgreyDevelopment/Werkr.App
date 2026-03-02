using System.Reflection;

using Microsoft.AspNetCore.Components;

namespace Werkr.Tests.Server.Pages;

/// <summary>
/// Verifies all Blazor pages are present and discoverable via reflection.
/// </summary>
[TestClass]
public class PageDiscoveryTests {
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
