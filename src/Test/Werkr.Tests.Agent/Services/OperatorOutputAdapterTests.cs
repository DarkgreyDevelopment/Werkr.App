using Werkr.Agent.Services;
using Werkr.Core.Communication;
using Werkr.Core.Cryptography;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Services;

/// <summary>
/// Unit tests for the <see cref="OperatorOutputAdapter"/> class.
/// Validates that <see cref="OperatorOutput"/> items streamed through
/// <see cref="OperatorOutputAdapter.StreamToGrpc"/> are correctly converted
/// to encrypted <see cref="EncryptedEnvelope"/> messages, preserving content,
/// timestamps, and key identifiers, and that empty streams, cancellation
/// tokens, and multi-item streams are handled correctly.
/// </summary>
[TestClass]
public class OperatorOutputAdapterTests {
    /// <summary>
    /// AES-256 shared key generated for each test run.
    /// </summary>
    private byte[] _sharedKey = null!;
    /// <summary>
    /// Key identifier embedded in every encrypted envelope.
    /// </summary>
    private const string TestKeyId = "test-key-1";

    /// <summary>
    /// Generates a fresh 256-bit random shared key before each test.
    /// </summary>
    [TestInitialize]
    public void TestInit( ) {
        _sharedKey = EncryptionProvider.GenerateRandomBytes( 32 );
    }

    /// <summary>
    /// Verifies that all items in the async stream are converted to
    /// <see cref="EncryptedEnvelope"/> messages and that each decrypted
    /// <see cref="GrpcLogMsg"/> contains the original log level and message.
    /// </summary>
    [TestMethod]
    public async Task StreamToGrpc_ConvertsAllItems( ) {
        List<OperatorOutput> items = [
            OperatorOutput.Create( "Information", "msg1" ),
            OperatorOutput.Create( "Warning", "msg2" ),
            OperatorOutput.Create( "Error", "msg3" ),
        ];
        MockServerStreamWriter<EncryptedEnvelope> writer = new( );

        await OperatorOutputAdapter.StreamToGrpc(
            ToAsyncEnumerable( items ),
            writer,
            _sharedKey,
            TestKeyId,
            CancellationToken.None
        );

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

    /// <summary>
    /// Verifies that the <see cref="OperatorOutput.Timestamp"/> field on the
    /// original <see cref="OperatorOutput"/> is preserved through encryption
    /// and decryption.
    /// </summary>
    [TestMethod]
    public async Task StreamToGrpc_PreservesTimestamps( ) {
        OperatorOutput item = OperatorOutput.Create( "Information", "hello" );
        MockServerStreamWriter<EncryptedEnvelope> writer = new( );

        await OperatorOutputAdapter.StreamToGrpc(
            ToAsyncEnumerable( [item] ),
            writer,
            _sharedKey,
            TestKeyId,
            CancellationToken.None
        );

        Assert.HasCount( 1, writer.Messages );
        GrpcLogMsg decrypted = PayloadEncryptor.DecryptFromEnvelope<GrpcLogMsg>(
            writer.Messages[0],
            _sharedKey
        );
        Assert.AreEqual( item.Timestamp, decrypted.Timestamp );
    }

    /// <summary>
    /// Verifies that the envelope ciphertext is non-empty and the key identifier
    /// matches <see cref="TestKeyId"/>, and that decryption recovers the
    /// original message.
    /// </summary>
    [TestMethod]
    public async Task StreamToGrpc_EncryptsOutput( ) {
        OperatorOutput item = OperatorOutput.Create( "Information", "secret payload" );
        MockServerStreamWriter<EncryptedEnvelope> writer = new( );

        await OperatorOutputAdapter.StreamToGrpc(
            ToAsyncEnumerable( [item] ),
            writer,
            _sharedKey,
            TestKeyId,
            CancellationToken.None
        );

        Assert.HasCount( 1, writer.Messages );

        EncryptedEnvelope envelope = writer.Messages[0];
        Assert.IsFalse( envelope.Ciphertext.IsEmpty );
        Assert.AreEqual( TestKeyId, envelope.KeyId );

        GrpcLogMsg decrypted = PayloadEncryptor.DecryptFromEnvelope<GrpcLogMsg>(
            envelope,
            _sharedKey
        );
        Assert.AreEqual( "secret payload", decrypted.Message );
    }

    /// <summary>
    /// Verifies that an empty async stream results in zero messages written
    /// to the <see cref="MockServerStreamWriter{T}"/>.
    /// </summary>
    [TestMethod]
    public async Task StreamToGrpc_EmptyStream_WritesNothing( ) {
        MockServerStreamWriter<EncryptedEnvelope> writer = new( );

        await OperatorOutputAdapter.StreamToGrpc(
            ToAsyncEnumerable( [] ),
            writer,
            _sharedKey,
            TestKeyId,
            CancellationToken.None
        );

        Assert.IsEmpty( writer.Messages );
    }

    /// <summary>
    /// Verifies that cancellation via a <see cref="CancellationTokenSource"/>
    /// stops the streaming loop within a reasonable number of iterations.
    /// </summary>
    [TestMethod]
    public async Task StreamToGrpc_CancellationStopsStreaming( ) {
        using CancellationTokenSource cts = new( );
        int itemCount = 0;

        async IAsyncEnumerable<OperatorOutput> InfiniteStream(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken ct = default
        ) {
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
                InfiniteStream( cts.Token ),
                writer,
                _sharedKey,
                TestKeyId,
                cts.Token
            );
        } catch (OperationCanceledException) {
            // Expected
        }

        Assert.IsLessThanOrEqualTo( 10, writer.Messages.Count, "Should have stopped streaming after cancellation." );
    }

    /// <summary>
    /// Converts a synchronous <see cref="IEnumerable{T}"/> of
    /// <see cref="OperatorOutput"/> items into an
    /// <see cref="IAsyncEnumerable{T}"/> with a yield between each item.
    /// </summary>
    private static async IAsyncEnumerable<OperatorOutput> ToAsyncEnumerable(
        IEnumerable<OperatorOutput> items
    ) {
        foreach (OperatorOutput item in items) {
            yield return item;
            await Task.Yield( );
        }
    }
}
