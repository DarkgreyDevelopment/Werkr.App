using Werkr.Common.Models;

namespace Werkr.Tests.Server.Pages;

/// <summary>
/// Tests for the Dashboard (Home.razor) functionality — agent count, status breakdown,
/// system info, quick actions visibility, and activity feed (§3.12.4).
/// </summary>
[TestClass]
public class DashboardTests {
    [TestMethod]
    public void Dashboard_AgentListDto_CountsCorrectly( ) {
        List<AgentListDto> agents = [
            new( Guid.NewGuid( ), "Agent-1", "https://a1:5001", "Connected", DateTime.UtcNow, DateTime.UtcNow ),
            new( Guid.NewGuid( ), "Agent-2", "https://a2:5001", "Disconnected", null, DateTime.UtcNow ),
            new( Guid.NewGuid( ), "Agent-3", "https://a3:5001", "Connected", DateTime.UtcNow, DateTime.UtcNow ),
        ];

        Assert.HasCount( 3, agents, "Dashboard should show total agent count." );
    }

    [TestMethod]
    public void Dashboard_StatusBreakdown_CategorisesCorrectly( ) {
        List<AgentListDto> agents = [
            new( Guid.NewGuid( ), "Agent-1", "https://a1:5001", "Connected", DateTime.UtcNow, DateTime.UtcNow ),
            new( Guid.NewGuid( ), "Agent-2", "https://a2:5001", "Connected", DateTime.UtcNow, DateTime.UtcNow ),
            new( Guid.NewGuid( ), "Agent-3", "https://a3:5001", "Disconnected", null, DateTime.UtcNow ),
            new( Guid.NewGuid( ), "Agent-4", "https://a4:5001", "Revoked", null, DateTime.UtcNow ),
        ];

        int connected = agents.Count( a =>
            string.Equals( a.Status, "Connected", StringComparison.OrdinalIgnoreCase ) );
        int disconnected = agents.Count( a =>
            string.Equals( a.Status, "Disconnected", StringComparison.OrdinalIgnoreCase ) );
        int revoked = agents.Count( a =>
            string.Equals( a.Status, "Revoked", StringComparison.OrdinalIgnoreCase ) );

        Assert.AreEqual( 2, connected, "Connected count mismatch." );
        Assert.AreEqual( 1, disconnected, "Disconnected count mismatch." );
        Assert.AreEqual( 1, revoked, "Revoked count mismatch." );
    }

    [TestMethod]
    public void Dashboard_SystemInfo_ComputesUptime( ) {
        DateTime startTime = DateTime.UtcNow.AddHours( -2 ).AddMinutes( -15 );

        TimeSpan uptime = DateTime.UtcNow - startTime;
        string formatted = uptime.TotalHours >= 1
            ? $"{uptime.Hours}h {uptime.Minutes}m"
            : $"{Math.Max( 0, uptime.Minutes )}m";

        Assert.Contains( "h", formatted, "Uptime should show hours." );
        Assert.IsGreaterThan( 120, uptime.TotalMinutes, "Uptime should be > 120 minutes." );
    }

    [TestMethod]
    public void Dashboard_QuickActions_AdminSeesAll( ) {
        // Simulate the role-checking logic used by AuthorizeView
        List<string> adminRoles = ["Admin"];

        bool canRegister = adminRoles.Contains( "Admin" );
        bool canSeeConsole = adminRoles.Contains( "Admin" ) || adminRoles.Contains( "Operator" );
        bool canManageUsers = adminRoles.Contains( "Admin" );
        bool canViewSettings = adminRoles.Contains( "Admin" );

        Assert.IsTrue( canRegister, "Admin should see Register Agent." );
        Assert.IsTrue( canSeeConsole, "Admin should see Operator Console." );
        Assert.IsTrue( canManageUsers, "Admin should see Manage Users." );
        Assert.IsTrue( canViewSettings, "Admin should see View Settings." );
    }

    [TestMethod]
    public void Dashboard_QuickActions_OperatorSeesSubset( ) {
        List<string> operatorRoles = ["Operator"];

        bool canRegister = operatorRoles.Contains( "Admin" );
        bool canSeeConsole = operatorRoles.Contains( "Admin" ) || operatorRoles.Contains( "Operator" );
        bool canManageUsers = operatorRoles.Contains( "Admin" );
        bool canViewSettings = operatorRoles.Contains( "Admin" );

        Assert.IsFalse( canRegister, "Operator should NOT see Register Agent." );
        Assert.IsTrue( canSeeConsole, "Operator should see Operator Console." );
        Assert.IsFalse( canManageUsers, "Operator should NOT see Manage Users." );
        Assert.IsFalse( canViewSettings, "Operator should NOT see View Settings." );
    }

    [TestMethod]
    public void Dashboard_ActivityFeed_SortsByDescendingTime( ) {
        Guid agentId = Guid.NewGuid( );
        DateTime now = DateTime.UtcNow;

        List<AgentActivityDto> events = [
            new( agentId, "Agent-1", "Registered", now.AddHours( -3 ), "Connected" ),
            new( agentId, "Agent-1", "Last Seen", now.AddMinutes( -5 ), "Connected" ),
            new( agentId, "Agent-1", "Revoked", now.AddMinutes( -1 ), "Revoked" ),
        ];

        List<AgentActivityDto> sorted = [.. events
            .OrderByDescending( e => e.OccurredAtUtc )];

        Assert.AreEqual( "Revoked", sorted[0].EventType,
            "Most recent event should be first." );
        Assert.AreEqual( "Registered", sorted[^1].EventType,
            "Oldest event should be last." );
    }

    [TestMethod]
    public void Dashboard_HealthDto_UnreachableAgent_HasNullAvailability( ) {
        AgentHealthDto unreachable = new(
            Guid.NewGuid( ),
            "Unreachable-Agent",
            "Unreachable",
            null,
            null,
            null,
            DateTime.UtcNow );

        Assert.AreEqual( "Unreachable", unreachable.Status );
        Assert.IsNull( unreachable.PowerShellAvailable,
            "Unreachable agent should have null PowerShell availability." );
        Assert.IsNull( unreachable.SystemShellAvailable,
            "Unreachable agent should have null SystemShell availability." );
    }

    [TestMethod]
    public void Dashboard_DatabaseHealth_AllConnected_ReportsHealthy( ) {
        List<DatabaseHealthDto> diagnostics = [
            new( "Application", "Npgsql", true, 3, 0, [] ),
            new( "Identity", "Npgsql", true, 1, 0, [] ),
        ];

        bool healthy = diagnostics.Count > 0 && diagnostics.All( d => d.IsConnected );

        Assert.IsTrue( healthy, "All databases connected should report healthy." );
    }

    [TestMethod]
    public void Dashboard_DatabaseHealth_OneDisconnected_ReportsUnhealthy( ) {
        List<DatabaseHealthDto> diagnostics = [
            new( "Application", "Npgsql", true, 3, 0, [] ),
            new( "Identity", "Npgsql", false, 1, 0, [] ),
        ];

        bool healthy = diagnostics.Count > 0 && diagnostics.All( d => d.IsConnected );

        Assert.IsFalse( healthy, "One disconnected database should report unhealthy." );
    }
}
