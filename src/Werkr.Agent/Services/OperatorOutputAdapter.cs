using Werkr.Core.Communication;

namespace Werkr.Agent.Services;

/// <summary>
/// Adapts <see cref="IAsyncEnumerable{OperatorOutput}"/> to gRPC <see cref="IServerStreamWriter{EncryptedEnvelope}"/>
/// with mandatory AES-256-GCM payload encryption via <see cref="EncryptedEnvelope"/>.
/// Each <see cref="OperatorOutput"/> is converted to a <see cref="GrpcLogMsg"/>,
/// serialized, encrypted, and written as an independent envelope frame.
/// </summary>
internal static class OperatorOutputAdapter {
    /// <summary>
    /// Streams operator output to a gRPC response writer, encrypting each message
    /// as an independent <see cref="EncryptedEnvelope"/>.
    /// </summary>
    /// <param name="outputs">The operator output stream.</param>
    /// <param name="responseStream">The gRPC server stream writer.</param>
    /// <param name="sharedKey">The AES-256 shared key for encryption. Must not be null.</param>
    /// <param name="keyId">Identifier for the shared key (supports key rotation).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sharedKey"/> is null.</exception>
    public static async Task StreamToGrpc(
        IAsyncEnumerable<OperatorOutput> outputs,
        IServerStreamWriter<EncryptedEnvelope> responseStream,
        byte[] sharedKey,
        string keyId,
        CancellationToken cancellationToken
    ) {

        ArgumentNullException.ThrowIfNull( sharedKey, nameof( sharedKey ) );

        await foreach (OperatorOutput output in outputs.WithCancellation( cancellationToken )) {
            GrpcLogMsg logMsg = new( ) {
                LogLevel = output.LogLevel,
                Message = output.Message,
                Timestamp = output.Timestamp,
            };

            EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope( logMsg, sharedKey, keyId );
            await responseStream.WriteAsync( envelope, cancellationToken );
        }
    }
}
