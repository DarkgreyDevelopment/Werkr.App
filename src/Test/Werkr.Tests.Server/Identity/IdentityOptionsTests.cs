using Werkr.Data.Identity.Extensions;

namespace Werkr.Tests.Server.Identity;

/// <summary>
/// Verifies that Identity is configured with NIST-aligned defaults.
/// </summary>
[TestClass]
public class IdentityOptionsTests {
    /// <summary>
    /// The <see cref="IdentityOptions"/> instance configured by <see
    /// cref="IdentityExtensions.ConfigureIdentityOptions"/> during test initialization.
    /// </summary>
    private readonly IdentityOptions _options = new( );

    /// <summary>
    /// Applies the <see cref="ConfigureIdentityOptions"/> configuration from <c>Werkr.Data.Identity</c> to the <see
    /// cref="_options"/> instance before each test runs.
    /// </summary>
    [TestInitialize]
    public void TestInit( ) {
        IdentityExtensions.ConfigureIdentityOptions( _options );
    }

    /// <summary>
    /// Verifies that the minimum required password length is set to 12 characters.
    /// </summary>
    [TestMethod]
    public void Password_RequiresMinLength12( ) {
        Assert.AreEqual( 12, _options.Password.RequiredLength );
    }

    /// <summary>
    /// Verifies that the password policy does not require at least one digit character.
    /// </summary>
    [TestMethod]
    public void Password_DoesNotRequireDigit( ) {
        Assert.IsFalse( _options.Password.RequireDigit );
    }

    /// <summary>
    /// Verifies that the password policy does not require at least one lowercase letter.
    /// </summary>
    [TestMethod]
    public void Password_DoesNotRequireLowercase( ) {
        Assert.IsFalse( _options.Password.RequireLowercase );
    }

    /// <summary>
    /// Verifies that the password policy does not require at least one uppercase letter.
    /// </summary>
    [TestMethod]
    public void Password_DoesNotRequireUppercase( ) {
        Assert.IsFalse( _options.Password.RequireUppercase );
    }

    /// <summary>
    /// Verifies that the password policy does not require at least one non-alphanumeric character.
    /// </summary>
    [TestMethod]
    public void Password_DoesNotRequireNonAlphanumeric( ) {
        Assert.IsFalse( _options.Password.RequireNonAlphanumeric );
    }

    /// <summary>
    /// Verifies that the password policy requires at least one unique character.
    /// </summary>
    [TestMethod]
    public void Password_RequiresUniqueChars1( ) {
        Assert.AreEqual( 1, _options.Password.RequiredUniqueChars );
    }

    /// <summary>
    /// Verifies that the account lockout duration is set to 15 minutes.
    /// </summary>
    [TestMethod]
    public void Lockout_15MinuteTimeSpan( ) {
        Assert.AreEqual( TimeSpan.FromMinutes( 15 ), _options.Lockout.DefaultLockoutTimeSpan );
    }

    /// <summary>
    /// Verifies that the maximum number of failed access attempts before lockout is set to 5.
    /// </summary>
    [TestMethod]
    public void Lockout_MaxFailedAttempts5( ) {
        Assert.AreEqual( 5, _options.Lockout.MaxFailedAccessAttempts );
    }

    /// <summary>
    /// Verifies that lockout is enabled for newly created user accounts.
    /// </summary>
    [TestMethod]
    public void Lockout_AllowedForNewUsers( ) {
        Assert.IsTrue( _options.Lockout.AllowedForNewUsers );
    }

    /// <summary>
    /// Verifies that the identity system requires unique email addresses across all users.
    /// </summary>
    [TestMethod]
    public void User_RequiresUniqueEmail( ) {
        Assert.IsTrue( _options.User.RequireUniqueEmail );
    }

    /// <summary>
    /// Verifies that the sign-in policy does not require a confirmed account (email confirmation) before allowing
    /// authentication.
    /// </summary>
    [TestMethod]
    public void SignIn_DoesNotRequireConfirmedAccount( ) {
        Assert.IsFalse( _options.SignIn.RequireConfirmedAccount );
    }
}
