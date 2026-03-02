using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Core.Cryptography;

namespace Werkr.Tests.Data.Unit.Communication;

/// <summary>
/// Tests that null SharedKey causes hard failure everywhere encryption is required.
/// Verifies Decision B2: null-encryption fallback is removed.
/// </summary>
[TestClass]
public class NullEncryptionTests {
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void EncryptToEnvelope_NullKey_ThrowsArgumentNullException( ) {
        HeartbeatRequest message = new( ) { StatusMessage = "test" };

        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            PayloadEncryptor.EncryptToEnvelope( message, null!, "key-1" ) );
    }

    [TestMethod]
    public void DecryptFromEnvelope_NullKey_ThrowsArgumentNullException( ) {
        byte[] validKey = EncryptionProvider.GenerateRandomBytes( 32 );
        HeartbeatRequest message = new( ) { StatusMessage = "test" };
        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope( message, validKey, "key-1" );

        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            PayloadEncryptor.DecryptFromEnvelope<HeartbeatRequest>( envelope, null! ) );
    }

    [TestMethod]
    public void DecryptFromEnvelope_KeyRotation_NullCurrentKey_ThrowsArgumentNullException( ) {
        byte[] validKey = EncryptionProvider.GenerateRandomBytes( 32 );
        HeartbeatRequest message = new( ) { StatusMessage = "test" };
        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope( message, validKey, "key-1" );

        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            PayloadEncryptor.DecryptFromEnvelope<HeartbeatRequest>(
                envelope, null!, "key-2", validKey, "key-1" ) );
    }

    [TestMethod]
    public void GrpcOutputReader_NullKey_ThrowsArgumentNullException( ) {
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) => {
            // ReadAsync is an async iterator, so enumerate to trigger the guard
            IAsyncEnumerable<OperatorOutput> reader = GrpcOutputReader.ReadAsync( null!, null!, TestContext.CancellationToken );
            IAsyncEnumerator<OperatorOutput> enumerator = reader.GetAsyncEnumerator( TestContext.CancellationToken );
            _ = enumerator.MoveNextAsync( ).AsTask( ).GetAwaiter( ).GetResult( );
        } );
    }
}
