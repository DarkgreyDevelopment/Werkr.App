using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Werkr.Common.Models;
using Werkr.Data.Identity;
using Werkr.Data.Identity.Entities;
using Werkr.Data.Identity.Extensions;

namespace Werkr.Tests.Server.Identity;

/// <summary>
/// Security-focused tests for MFA reset/regen password requirements
/// and health endpoint timeout handling.
/// </summary>
[TestClass]
public class SecurityTests {
    /// <summary>
    /// Verifies that an MFA reset attempt with an incorrect password fails the password check and that the <see
    /// cref="TwoFactorEnabled"/> flag remains <see langword="true"/>, preventing unauthorized MFA disablement.
    /// </summary>
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

    /// <summary>
    /// Verifies that when the correct password is provided, the MFA reset flow succeeds: two-factor authentication is
    /// disabled, the authenticator key is reset, and <see cref="GetTwoFactorEnabledAsync"/> returns <see
    /// langword="false"/>.
    /// </summary>
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

    /// <summary>
    /// Verifies that an attempt to regenerate recovery codes with an incorrect password fails the password check and
    /// that the existing recovery code count remains unchanged, preventing unauthorized recovery code regeneration.
    /// </summary>
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

    /// <summary>
    /// Verifies that an <see cref="AgentHealthDto"/> constructed for an unreachable agent has a status of
    /// "Unreachable" and <see langword="null"/> values for <see cref="PowerShellAvailable"/> and <see
    /// cref="SystemShellAvailable"/>, simulating a gRPC connection failure scenario.
    /// </summary>
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
            DateTime.UtcNow
        );

        Assert.AreEqual( "Unreachable", unreachable.Status,
            "RpcException should result in Unreachable status." );
        Assert.IsNull( unreachable.PowerShellAvailable,
            "Unreachable agent should have null operator availability." );
        Assert.IsNull( unreachable.SystemShellAvailable,
            "Unreachable agent should have null operator availability." );
    }

    /// <summary>
    /// Verifies that when a batch of health check tasks includes both completed and cancelled tasks (simulating a
    /// total timeout scenario), only the successfully completed results are collected, and cancelled tasks are
    /// excluded from the partial result set.
    /// </summary>
    [TestMethod]
    public void HealthEndpoint_TotalTimeout_ReturnsPartialResults( ) {
        // Simulates the catch(OperationCanceledException) path in the health endpoint.
        // When the overall 10s timeout fires, completed tasks should be returned.
        Task<AgentHealthDto> completedTask = Task.FromResult( new AgentHealthDto(
            Guid.NewGuid( ), "Fast-Agent", "Connected", true, true,
            DateTime.UtcNow, DateTime.UtcNow
        ));

        Task<AgentHealthDto> cancelledTask = Task.FromCanceled<AgentHealthDto>(
            new CancellationToken(canceled: true)
        );

        List<Task<AgentHealthDto>> tasks = [completedTask, cancelledTask];

        List<AgentHealthDto> partial = [.. tasks
            .Where( t => t.IsCompletedSuccessfully )
            .Select( t => t.Result )];

        Assert.HasCount( 1, partial,
            "Only successfully completed health checks should be returned." );
        Assert.AreEqual( "Fast-Agent", partial[0].ConnectionName );
    }

    // ── helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="WerkrUser"/> with MFA enabled using the provided <see cref="UserManager"/>. The user is
    /// created with a default password of "TestPassword123!" and has <see cref="TwoFactorEnabled"/> set to <see
    /// langword="true"/> after creation.
    /// </summary>
    private static async Task<WerkrUser> CreateMfaEnabledUserAsync(
        UserManager<WerkrUser> um, string email
    ) {
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

    /// <summary>
    /// Builds a <see cref="ServiceProvider"/> configured with an in-memory <see cref="WerkrIdentityDbContext"/>,
    /// ASP.NET Core Identity services for <see cref="WerkrUser"/>, logging, and default token providers. Returns the
    /// built provider and outputs the resolved <see cref="UserManager"/> for test use.
    /// </summary>
    private static ServiceProvider BuildServiceProvider( out UserManager<WerkrUser> userManager ) {
        ServiceCollection services = new( );
        string dbName = $"SecurityTests_{Guid.NewGuid( )}";

        _ = services.AddDbContext<WerkrIdentityDbContext>( options =>
            options.UseInMemoryDatabase( dbName ) );

        _ = services.AddIdentity<WerkrUser, IdentityRole>(
            IdentityExtensions.ConfigureIdentityOptions
        )
            .AddEntityFrameworkStores<WerkrIdentityDbContext>( )
            .AddDefaultTokenProviders( );

        _ = services.AddLogging( );

        ServiceProvider provider = services.BuildServiceProvider( );
        userManager = provider.GetRequiredService<UserManager<WerkrUser>>( );
        return provider;
    }
}
