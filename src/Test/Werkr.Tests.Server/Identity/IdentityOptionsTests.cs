using Microsoft.AspNetCore.Identity;

using Werkr.Data.Identity.Extensions;

namespace Werkr.Tests.Server.Identity;

/// <summary>
/// Verifies that Identity is configured with NIST-aligned defaults.
/// </summary>
[TestClass]
public class IdentityOptionsTests {
    private readonly IdentityOptions _options = new( );

    [TestInitialize]
    public void TestInit( ) {
        IdentityExtensions.ConfigureIdentityOptions( _options );
    }

    [TestMethod]
    public void Password_RequiresMinLength12( ) {
        Assert.AreEqual( 12, _options.Password.RequiredLength );
    }

    [TestMethod]
    public void Password_DoesNotRequireDigit( ) {
        Assert.IsFalse( _options.Password.RequireDigit );
    }

    [TestMethod]
    public void Password_DoesNotRequireLowercase( ) {
        Assert.IsFalse( _options.Password.RequireLowercase );
    }

    [TestMethod]
    public void Password_DoesNotRequireUppercase( ) {
        Assert.IsFalse( _options.Password.RequireUppercase );
    }

    [TestMethod]
    public void Password_DoesNotRequireNonAlphanumeric( ) {
        Assert.IsFalse( _options.Password.RequireNonAlphanumeric );
    }

    [TestMethod]
    public void Password_RequiresUniqueChars1( ) {
        Assert.AreEqual( 1, _options.Password.RequiredUniqueChars );
    }

    [TestMethod]
    public void Lockout_15MinuteTimeSpan( ) {
        Assert.AreEqual( TimeSpan.FromMinutes( 15 ), _options.Lockout.DefaultLockoutTimeSpan );
    }

    [TestMethod]
    public void Lockout_MaxFailedAttempts5( ) {
        Assert.AreEqual( 5, _options.Lockout.MaxFailedAccessAttempts );
    }

    [TestMethod]
    public void Lockout_AllowedForNewUsers( ) {
        Assert.IsTrue( _options.Lockout.AllowedForNewUsers );
    }

    [TestMethod]
    public void User_RequiresUniqueEmail( ) {
        Assert.IsTrue( _options.User.RequireUniqueEmail );
    }

    [TestMethod]
    public void SignIn_DoesNotRequireConfirmedAccount( ) {
        Assert.IsFalse( _options.SignIn.RequireConfirmedAccount );
    }
}
