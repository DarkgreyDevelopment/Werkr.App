using Werkr.Agent.Protos;
using Werkr.Agent.Services;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Core.Cryptography;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Services;

[TestClass]
public class OperatorOutputAdapterTests {
    private byte[] _sharedKey = null!;
    private const string TestKeyId = "test-key-1";

    [TestInitialize]
    public void TestInit( ) {
        _sharedKey = EncryptionProvider.GenerateRandomBytes( 32 );
    }

    [TestMethod]
    public async Task StreamToGrpc_ConvertsAllItems( ) {
        List<OperatorOutput> items = [
            OperatorOutput.Create( "Information", "msg1" ),
            OperatorOutput.Create( "Warning", "msg2" ),
            OperatorOutput.Create( "Error", "msg3" ),
        ];
        MockServerStreamWriter<EncryptedEnvelope> writer = new( );

        await OperatorOutputAdapter.StreamToGrpc(
            ToAsyncEnumerable( items ), writer, _sharedKey, TestKeyId, CancellationToken.None );

        Assert.HasCount( 3, writer.Messages );

        GrpcLogMsg msg0 = PayloadEncryptor.DecryptFromEnvelope<GrpcLogMsg>( writer.Messages[0], _sharedKey );
        GrpcLogMsg msg1 = PayloadEncryptor.DecryptFromEnvelope<GrpcLogMsg>( writer.Messages[1], _sharedKey );
        GrpcLogMsg msg2 = PayloadEncryptor.DecryptFromEnvelope<GrpcLogMsg>( writer.Messages[2], _sharedKey );

        Assert.AreEqual( "Information", msg0.LogLevel );
        Assert.AreEqual( "msg1", msg0.Message );
        Assert.AreEqual( "Warning", msg1.LogLevel );
        Assert.AreEqual( "msg2", msg1.Message );
        Assert.AreEqual( "Error", msg2.LogLevel );
        Assert.AreEqual( "msg3", msg2.Message );
    }

    [TestMethod]
    public async Task StreamToGrpc_PreservesTimestamps( ) {
        OperatorOutput item = OperatorOutput.Create( "Information", "hello" );
        MockServerStreamWriter<EncryptedEnvelope> writer = new( );

        await OperatorOutputAdapter.StreamToGrpc(
            ToAsyncEnumerable( [item] ), writer, _sharedKey, TestKeyId, CancellationToken.None );

        Assert.HasCount( 1, writer.Messages );
        GrpcLogMsg decrypted = PayloadEncryptor.DecryptFromEnvelope<GrpcLogMsg>( writer.Messages[0], _sharedKey );
        Assert.AreEqual( item.Timestamp, decrypted.Timestamp );
    }

    [TestMethod]
    public async Task StreamToGrpc_EncryptsOutput( ) {
        OperatorOutput item = OperatorOutput.Create( "Information", "secret payload" );
        MockServerStreamWriter<EncryptedEnvelope> writer = new( );

        await OperatorOutputAdapter.StreamToGrpc(
            ToAsyncEnumerable( [item] ), writer, _sharedKey, TestKeyId, CancellationToken.None );

        Assert.HasCount( 1, writer.Messages );

        EncryptedEnvelope envelope = writer.Messages[0];
        Assert.IsFalse( envelope.Ciphertext.IsEmpty );
        Assert.AreEqual( TestKeyId, envelope.KeyId );

        GrpcLogMsg decrypted = PayloadEncryptor.DecryptFromEnvelope<GrpcLogMsg>( envelope, _sharedKey );
        Assert.AreEqual( "secret payload", decrypted.Message );
    }

    [TestMethod]
    public async Task StreamToGrpc_EmptyStream_WritesNothing( ) {
        MockServerStreamWriter<EncryptedEnvelope> writer = new( );

        await OperatorOutputAdapter.StreamToGrpc(
            ToAsyncEnumerable( [] ), writer, _sharedKey, TestKeyId, CancellationToken.None );

        Assert.IsEmpty( writer.Messages );
    }

    [TestMethod]
    public async Task StreamToGrpc_CancellationStopsStreaming( ) {
        using CancellationTokenSource cts = new( );
        int itemCount = 0;

        async IAsyncEnumerable<OperatorOutput> InfiniteStream(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default ) {
            while (!ct.IsCancellationRequested) {
                yield return OperatorOutput.Create( "Information", $"item {itemCount++}" );
                await Task.Yield( );
                if (itemCount >= 5) {
                    await cts.CancelAsync( );
                }
            }
        }

        MockServerStreamWriter<EncryptedEnvelope> writer = new( );

        try {
            await OperatorOutputAdapter.StreamToGrpc(
                InfiniteStream( cts.Token ), writer, _sharedKey, TestKeyId, cts.Token );
        } catch (OperationCanceledException) {
            // Expected
        }

        Assert.IsLessThanOrEqualTo( 10, writer.Messages.Count, "Should have stopped streaming after cancellation." );
    }

    private static async IAsyncEnumerable<OperatorOutput> ToAsyncEnumerable(
        IEnumerable<OperatorOutput> items ) {
        foreach (OperatorOutput item in items) {
            yield return item;
            await Task.Yield( );
        }
    }
}
