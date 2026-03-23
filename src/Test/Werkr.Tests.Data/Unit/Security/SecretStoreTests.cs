using Werkr.Core.Security;

namespace Werkr.Tests.Data.Unit.Security;

/// <summary>
/// Contains unit tests for the <see cref="SecretStore"/> and <see cref="SecretStoreFactory"/> classes defined in
/// Werkr.Core. Tests run only on Windows via the <see cref="OSCondition"/> attribute. Validates storing, retrieving,
/// and deleting secrets from the platform secret store.
/// </summary>
[TestClass]
public class SecretStoreTests {
    /// <summary>
    /// Verifies that a secret written with <see cref="SetSecretAsync"/> can be read back with <see
    /// cref="GetSecretAsync"/>.
    /// </summary>
    [TestMethod]
    [OSCondition( OperatingSystems.Windows )]
    public async Task SetAndGetSecret_RoundTrip_ReturnsStoredValue( ) {
        ISecretStore store = SecretStoreFactory.Create( );
        string key = "werkr_test_" + Guid.NewGuid( ).ToString( "N" );
        string value = "test-secret-" + Guid.NewGuid( ).ToString( );

        try {
            await store.SetSecretAsync(
                key,
                value
            );
            string? retrieved = await store.GetSecretAsync( key );
            Assert.AreEqual(
                value,
                retrieved
            );
        } finally {
            await store.DeleteSecretAsync( key );
        }
    }

    /// <summary>
    /// Verifies that <see cref="GetSecretAsync"/> returns <see langword="null"/> for a key that has not been stored.
    /// </summary>
    [TestMethod]
    [OSCondition( OperatingSystems.Windows )]
    public async Task GetSecret_NonExistentKey_ReturnsNull( ) {
        ISecretStore store = SecretStoreFactory.Create( );
        string key = "werkr_test_nonexistent_" + Guid.NewGuid( ).ToString( "N" );

        string? result = await store.GetSecretAsync( key );

        Assert.IsNull( result );
    }

    /// <summary>
    /// Verifies that <see cref="DeleteSecretAsync"/> removes the stored value so that subsequent retrieval returns <see
    /// langword="null"/>.
    /// </summary>
    [TestMethod]
    [OSCondition( OperatingSystems.Windows )]
    public async Task DeleteSecret_RemovesStoredValue( ) {
        ISecretStore store = SecretStoreFactory.Create( );
        string key = "werkr_test_" + Guid.NewGuid( ).ToString( "N" );

        await store.SetSecretAsync(
            key,
            "to-be-deleted"
        );
        await store.DeleteSecretAsync( key );
        string? result = await store.GetSecretAsync( key );

        Assert.IsNull( result );
    }
}
