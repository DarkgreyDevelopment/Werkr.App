using Werkr.Common.Models;
using Werkr.Core.Registration;
using Werkr.Core.Registration.Models;
using Werkr.Data.Entities.Registration;

namespace Werkr.Tests.Data.Unit.Registration;

/// <summary>
/// Contains unit tests for the <see cref="RegistrationBundleGenerator.CreateBundle"/> static method defined in
/// Werkr.Core. Validates entity population, default/custom expiration, and encrypted payload decryptability.
/// </summary>
[TestClass]
public class RegistrationBundleGeneratorTests {
    /// <summary>
    /// Verifies that <see cref="CreateBundle"/> with default settings produces a valid entity with the expected
    /// connection name, bundle ID length, status, and key size.
    /// </summary>
    [TestMethod]
    public void CreateBundle_DefaultSettings_ProducesValidEntity( ) {
        (string encrypted, RegistrationBundle entity) = RegistrationBundleGenerator.CreateBundle(
            "TestConn",
            "https://server:5000",
            "password123"
        );

        Assert.IsNotNull( encrypted );
        Assert.IsNotNull( entity );
        Assert.AreEqual(
            "TestConn",
            entity.ConnectionName
        );
        Assert.HasCount(
            16,
            entity.BundleId
        );
        Assert.AreEqual(
            RegistrationStatus.Pending,
            entity.Status
        );
        Assert.AreEqual(
            4096,
            entity.KeySize
        );
    }

    /// <summary>
    /// Verifies that the default expiration is approximately 24 hours from now.
    /// </summary>
    [TestMethod]
    public void CreateBundle_DefaultExpiration_ExpiresIn24Hours( ) {
        DateTime before = DateTime.UtcNow.AddHours( 24 );

        (_, RegistrationBundle entity) = RegistrationBundleGenerator.CreateBundle(
            "Conn",
            "https://server",
            "pass"
        );

        DateTime after = DateTime.UtcNow.AddHours( 24 );

        // MSTest v4: IsGreaterThanOrEqualTo(lowerBound, value) asserts value >= lowerBound
        Assert.IsGreaterThanOrEqualTo(
            before,
            entity.ExpiresAt
        );
        Assert.IsLessThanOrEqualTo(
            after,
            entity.ExpiresAt
        );
    }

    /// <summary>
    /// Verifies that a custom expiration <see cref="TimeSpan"/> correctly sets the entity's expiry time.
    /// </summary>
    [TestMethod]
    public void CreateBundle_CustomExpiration_SetsCorrectExpiry( ) {
        TimeSpan customExpiration = TimeSpan.FromMinutes( 30 );
        DateTime before = DateTime.UtcNow.AddMinutes( 30 );

        (_, RegistrationBundle entity) = RegistrationBundleGenerator.CreateBundle(
            "Conn",
            "https://server",
            "pass",
            expiration: customExpiration
        );

        DateTime after = DateTime.UtcNow.AddMinutes( 30 );

        // MSTest v4: IsGreaterThanOrEqualTo(lowerBound, value) asserts value >= lowerBound
        Assert.IsGreaterThanOrEqualTo(
            before,
            entity.ExpiresAt
        );
        Assert.IsLessThanOrEqualTo(
            after,
            entity.ExpiresAt
        );
    }

    /// <summary>
    /// Verifies that the encrypted bundle string can be decrypted with the same password and the resulting <see
    /// cref="RegistrationBundlePayload"/> matches the entity.
    /// </summary>
    [TestMethod]
    public void CreateBundle_EncryptedBundle_DecryptableWithPassword( ) {
        string password = "MySecurePass!";

        (string encrypted, RegistrationBundle entity) = RegistrationBundleGenerator.CreateBundle(
            "TestConn",
            "https://server:5000",
            password
        );

        RegistrationBundlePayload payload = RegistrationBundlePayload.FromEncryptedString(
            encrypted,
            password
        );

        CollectionAssert.AreEqual(
            entity.BundleId,
            payload.BundleId
        );
        Assert.AreEqual(
            "TestConn",
            payload.ConnectionName
        );
        Assert.AreEqual(
            "https://server:5000",
            payload.ServerUrl
        );
    }
}
