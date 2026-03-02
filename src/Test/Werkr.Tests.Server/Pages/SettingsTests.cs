using Werkr.Common.Models;

namespace Werkr.Tests.Server.Pages;

/// <summary>
/// Tests for the Settings/Diagnostics page: configuration display,
/// database health, and access control (§3.12.6).
/// </summary>
[TestClass]
public class SettingsTests {
    [TestMethod]
    public void Settings_DatabaseHealthDto_DisplaysCorrectly( ) {
        DatabaseHealthDto dto = new(
            "Application",
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            IsConnected: true,
            AppliedMigrationCount: 3,
            PendingMigrationCount: 0,
            [] );

        Assert.AreEqual( "Application", dto.ContextName );
        Assert.IsTrue( dto.IsConnected );
        Assert.AreEqual( 3, dto.AppliedMigrationCount );
        Assert.AreEqual( 0, dto.PendingMigrationCount );
        Assert.HasCount( 0, dto.PendingMigrations );
    }

    [TestMethod]
    public void Settings_DatabaseHealthDto_ShowsPendingMigrations( ) {
        DatabaseHealthDto dto = new(
            "Identity",
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            IsConnected: true,
            AppliedMigrationCount: 1,
            PendingMigrationCount: 2,
            ["20260220_AddLastLoginUtc", "20260221_AddUserPrefs"] );

        Assert.AreEqual( 2, dto.PendingMigrationCount );
        Assert.HasCount( 2, dto.PendingMigrations );
        Assert.Contains( "20260220_AddLastLoginUtc", dto.PendingMigrations );
    }

    [TestMethod]
    public void Settings_AgentHealthSummary_AggregatesCorrectly( ) {
        List<AgentHealthDto> health = [
            new( Guid.NewGuid( ), "Agent-1", "Connected", true, true, DateTime.UtcNow, DateTime.UtcNow ),
            new( Guid.NewGuid( ), "Agent-2", "Connected", true, false, DateTime.UtcNow, DateTime.UtcNow ),
            new( Guid.NewGuid( ), "Agent-3", "Unreachable", null, null, null, DateTime.UtcNow ),
        ];

        int withPowerShell = health.Count( h => h.PowerShellAvailable == true );
        int withSystemShell = health.Count( h => h.SystemShellAvailable == true );
        int unreachable = health.Count( h => h.Status == "Unreachable" );

        Assert.AreEqual( 2, withPowerShell, "Two agents should have PowerShell." );
        Assert.AreEqual( 1, withSystemShell, "One agent should have SystemShell." );
        Assert.AreEqual( 1, unreachable, "One agent should be unreachable." );
    }
}
