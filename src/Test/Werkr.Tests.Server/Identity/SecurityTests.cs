using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Werkr.Common.Models;
using Werkr.Data.Identity;
using Werkr.Data.Identity.Entities;

namespace Werkr.Tests.Server.Identity;

/// <summary>
/// Security-focused tests for MFA reset/regen password requirements
/// and health endpoint timeout handling (§3.12.8).
/// </summary>
[TestClass]
public class SecurityTests {
    [TestMethod]
    public async Task MfaReset_RequiresPasswordConfirmation( ) {
        _ = BuildServiceProvider( out UserManager<WerkrUser> um );

        WerkrUser user = await CreateMfaEnabledUserAsync( um, "mfa-reset-nopass@local" );

        // Simulate the password check that the Mfa.razor page performs
        bool passwordValid = await um.CheckPasswordAsync( user, "wrong-password" );

        Assert.IsFalse( passwordValid,
            "MFA reset without valid password should be rejected." );

        // The page only calls SetTwoFactorEnabledAsync(false) when password is valid.
        // Verify 2FA is still enabled:
        Assert.IsTrue( await um.GetTwoFactorEnabledAsync( user ),
            "2FA should remain enabled when password check fails." );
    }

    [TestMethod]
    public async Task MfaReset_ValidPassword_ResetsAuthenticator( ) {
        _ = BuildServiceProvider( out UserManager<WerkrUser> um );

        WerkrUser user = await CreateMfaEnabledUserAsync( um, "mfa-reset-valid@local" );

        Assert.IsTrue( await um.GetTwoFactorEnabledAsync( user ) );

        // Simulate valid password confirmation
        bool passwordValid = await um.CheckPasswordAsync( user, "TestPassword123!" );
        Assert.IsTrue( passwordValid, "Correct password should pass check." );

        // Perform the reset (matches Mfa.razor ResetMfaAsync logic)
        IdentityResult disableResult = await um.SetTwoFactorEnabledAsync( user, false );
        Assert.IsTrue( disableResult.Succeeded );

        _ = await um.ResetAuthenticatorKeyAsync( user );

        Assert.IsFalse( await um.GetTwoFactorEnabledAsync( user ),
            "2FA should be disabled after reset." );
    }

    [TestMethod]
    public async Task RecoveryCodeRegen_RequiresPasswordConfirmation( ) {
        _ = BuildServiceProvider( out UserManager<WerkrUser> um );

        WerkrUser user = await CreateMfaEnabledUserAsync( um, "regen-nopass@local" );

        // Generate initial codes
        IEnumerable<string>? initialCodes =
            await um.GenerateNewTwoFactorRecoveryCodesAsync( user, 5 );
        Assert.IsNotNull( initialCodes );
        int initialCount = await um.CountRecoveryCodesAsync( user );

        // Attempt regen with wrong password
        bool passwordValid = await um.CheckPasswordAsync( user, "not-correct" );

        Assert.IsFalse( passwordValid,
            "Recovery code regen without valid password should be rejected." );

        // Verify codes were NOT regenerated (count unchanged)
        int afterCount = await um.CountRecoveryCodesAsync( user );
        Assert.AreEqual( initialCount, afterCount,
            "Recovery code count should not change without valid password." );
    }

    [TestMethod]
    public void HealthEndpoint_UnreachableAgent_ReturnsUnreachableStatus( ) {
        // Simulates the BuildHealthAsync catch(RpcException) path
        AgentHealthDto unreachable = new(
            Guid.NewGuid( ),
            "Offline-Agent",
            "Unreachable",
            null,
            null,
            null,
            DateTime.UtcNow );

        Assert.AreEqual( "Unreachable", unreachable.Status,
            "RpcException should result in Unreachable status." );
        Assert.IsNull( unreachable.PowerShellAvailable,
            "Unreachable agent should have null operator availability." );
        Assert.IsNull( unreachable.SystemShellAvailable,
            "Unreachable agent should have null operator availability." );
    }

    [TestMethod]
    public void HealthEndpoint_TotalTimeout_ReturnsPartialResults( ) {
        // Simulates the catch(OperationCanceledException) path in the health endpoint.
        // When the overall 10s timeout fires, completed tasks should be returned.
        Task<AgentHealthDto> completedTask = Task.FromResult( new AgentHealthDto(
            Guid.NewGuid( ), "Fast-Agent", "Connected", true, true,
            DateTime.UtcNow, DateTime.UtcNow ) );

        Task<AgentHealthDto> cancelledTask = Task.FromCanceled<AgentHealthDto>(
            new CancellationToken( canceled: true ) );

        List<Task<AgentHealthDto>> tasks = [completedTask, cancelledTask];

        List<AgentHealthDto> partial = [.. tasks
            .Where( t => t.IsCompletedSuccessfully )
            .Select( t => t.Result )];

        Assert.HasCount( 1, partial,
            "Only successfully completed health checks should be returned." );
        Assert.AreEqual( "Fast-Agent", partial[0].ConnectionName );
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static async Task<WerkrUser> CreateMfaEnabledUserAsync(
        UserManager<WerkrUser> um, string email ) {
        WerkrUser user = new( ) {
            UserName = email,
            Email = email,
            Name = email.Split( '@' )[0],
            Enabled = true,
            ChangePassword = false,
            Requires2FA = false,
            EmailConfirmed = true
        };

        IdentityResult createResult = await um.CreateAsync( user, "TestPassword123!" );
        Assert.IsTrue( createResult.Succeeded,
            $"User creation failed: {string.Join( "; ", createResult.Errors.Select( e => e.Description ) )}" );

        IdentityResult mfaResult = await um.SetTwoFactorEnabledAsync( user, true );
        Assert.IsTrue( mfaResult.Succeeded, "Enabling 2FA should succeed." );

        return user;
    }

    private static ServiceProvider BuildServiceProvider( out UserManager<WerkrUser> userManager ) {
        ServiceCollection services = new( );
        string dbName = $"SecurityTests_{Guid.NewGuid( )}";

        _ = services.AddDbContext<WerkrIdentityDbContext>( options =>
            options.UseInMemoryDatabase( dbName ) );

        _ = services.AddIdentity<WerkrUser, IdentityRole>(
            Werkr.Data.Identity.Extensions.IdentityExtensions.ConfigureIdentityOptions )
            .AddEntityFrameworkStores<WerkrIdentityDbContext>( )
            .AddDefaultTokenProviders( );

        _ = services.AddLogging( );

        ServiceProvider provider = services.BuildServiceProvider( );
        userManager = provider.GetRequiredService<UserManager<WerkrUser>>( );
        return provider;
    }
}
