using Google.Protobuf;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Werkr.Common.Communication;
using Werkr.Common.Protos;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Core.Communication;

/// <summary>
/// Centralizes the gRPC request decryption and response encryption lifecycle.
/// Replaces the duplicated GetConnection/keyId/PayloadEncryptor pattern across
/// all API-hosted gRPC services. On response encryption, checks the notification
/// outbox and sets ResponseMetadata.UrgentCommandsPending so agents can trigger
/// an immediate heartbeat.
/// </summary>
public sealed class SecureResponseBuilder( IServiceScopeFactory scopeFactory ) {

    /// <summary>
    /// Extracts the authenticated connection from context and decrypts the request envelope.
    /// </summary>
    public static (RegisteredConnection Connection, T Message) DecryptRequest<T>(
        EncryptedEnvelope envelope,
        ServerCallContext context )
        where T : IMessage<T>, new( ) {

        RegisteredConnection connection = GetConnection( context );
        T message = PayloadEncryptor.DecryptFromEnvelope<T>(
            envelope, connection.SharedKey );
        return (connection, message);
    }

    /// <summary>
    /// Checks for pending outbox notifications, sets ResponseMetadata, and encrypts
    /// the response into an EncryptedEnvelope.
    /// </summary>
    public async Task<EncryptedEnvelope> EncryptResponseAsync<T>(
        T response,
        RegisteredConnection connection,
        CancellationToken ct )
        where T : IMessage<T>, IHasResponseMetadata {

        bool hasPending = await HasPendingNotificationsAsync( connection.Id, ct );
        response.Metadata = new ResponseMetadata { UrgentCommandsPending = hasPending };

        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );
        return PayloadEncryptor.EncryptToEnvelope( response, connection.SharedKey, keyId );
    }

    /// <summary>
    /// Encrypts a response without metadata (for contexts where no outbox applies,
    /// e.g., registration using a password-derived key).
    /// </summary>
    public static EncryptedEnvelope EncryptResponse<T>(
        T response,
        byte[] sharedKey,
        string keyId )
        where T : IMessage<T> {

        return PayloadEncryptor.EncryptToEnvelope( response, sharedKey, keyId );
    }

    /// <summary>
    /// Extracts the authenticated <see cref="RegisteredConnection"/> from the gRPC context.
    /// Set by <c>AgentBearerTokenInterceptor</c>.
    /// </summary>
    public static RegisteredConnection GetConnection( ServerCallContext context ) {
        return context.UserState.TryGetValue( "Connection", out object? connObj )
            && connObj is RegisteredConnection connection
            ? connection
            : throw new RpcException( new Status( StatusCode.Internal,
                "Connection not resolved by interceptor." ) );
    }

    private async Task<bool> HasPendingNotificationsAsync(
        Guid connectionId, CancellationToken ct ) {

        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        return await db.PendingAgentNotifications
            .AnyAsync( n => n.ConnectionId == connectionId
                        && n.ExpiresUtc > DateTime.UtcNow, ct );
    }
}
