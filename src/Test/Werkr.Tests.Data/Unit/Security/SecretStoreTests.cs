using Werkr.Core.Security;

namespace Werkr.Tests.Data.Unit.Security;

[TestClass]
public class SecretStoreTests {
    [TestMethod]
    [OSCondition( OperatingSystems.Windows )]
    public async Task SetAndGetSecret_RoundTrip_ReturnsStoredValue( ) {
        ISecretStore store = SecretStoreFactory.Create( );
        string key = "werkr_test_" + Guid.NewGuid( ).ToString( "N" );
        string value = "test-secret-" + Guid.NewGuid( ).ToString( );

        try {
            await store.SetSecretAsync( key, value );
            string? retrieved = await store.GetSecretAsync( key );
            Assert.AreEqual( value, retrieved );
        } finally {
            await store.DeleteSecretAsync( key );
        }
    }

    [TestMethod]
    [OSCondition( OperatingSystems.Windows )]
    public async Task GetSecret_NonExistentKey_ReturnsNull( ) {
        ISecretStore store = SecretStoreFactory.Create( );
        string key = "werkr_test_nonexistent_" + Guid.NewGuid( ).ToString( "N" );

        string? result = await store.GetSecretAsync( key );

        Assert.IsNull( result );
    }

    [TestMethod]
    [OSCondition( OperatingSystems.Windows )]
    public async Task DeleteSecret_RemovesStoredValue( ) {
        ISecretStore store = SecretStoreFactory.Create( );
        string key = "werkr_test_" + Guid.NewGuid( ).ToString( "N" );

        await store.SetSecretAsync( key, "to-be-deleted" );
        await store.DeleteSecretAsync( key );
        string? result = await store.GetSecretAsync( key );

        Assert.IsNull( result );
    }
}
