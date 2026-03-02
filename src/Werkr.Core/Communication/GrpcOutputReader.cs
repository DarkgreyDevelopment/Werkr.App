using System.Runtime.CompilerServices;

using Grpc.Core;

using Werkr.Agent.Protos;
using Werkr.Common.Protos;

namespace Werkr.Core.Communication;

/// <summary>
/// Reads a gRPC server-streaming response of <see cref="EncryptedEnvelope"/> messages,
/// decrypts each envelope to a <see cref="GrpcLogMsg"/>, and yields
/// <see cref="OperatorOutput"/> records.
/// </summary>
public static class GrpcOutputReader {
    /// <summary>
    /// Reads encrypted <see cref="EncryptedEnvelope"/> messages from the response stream,
    /// decrypts them to <see cref="GrpcLogMsg"/> using the connection's SharedKey,
    /// and yields <see cref="OperatorOutput"/>.
    /// </summary>
    /// <param name="responseStream">The gRPC response stream reader.</param>
    /// <param name="sharedKey">The AES-256 shared key for payload decryption. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of decrypted <see cref="OperatorOutput"/> records.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sharedKey"/> is null.</exception>
    public static async IAsyncEnumerable<OperatorOutput> ReadAsync(
        IAsyncStreamReader<EncryptedEnvelope> responseStream,
        byte[] sharedKey,
        [EnumeratorCancellation] CancellationToken cancellationToken = default ) {

        ArgumentNullException.ThrowIfNull( sharedKey, nameof( sharedKey ) );

        await foreach (EncryptedEnvelope envelope in responseStream.ReadAllAsync( cancellationToken )) {
            GrpcLogMsg msg = PayloadEncryptor.DecryptFromEnvelope<GrpcLogMsg>( envelope, sharedKey );
            yield return new OperatorOutput( msg.LogLevel, msg.Message, msg.Timestamp );
        }
    }
}
