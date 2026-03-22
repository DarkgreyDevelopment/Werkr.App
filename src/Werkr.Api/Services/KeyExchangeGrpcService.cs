using System.Security.Cryptography;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Api.Services;

/// <summary>
/// API-hosted gRPC service for two-phase key rotation.
/// Agents call <see cref="FetchPendingKey"/> to check for pending rotations
/// and <see cref="AcknowledgeKey"/> to confirm activation of the new key.
/// All RPCs use <see cref="EncryptedEnvelope"/>.
/// </summary>
/// <param name="scopeFactory">Service scope factory for per-call database contexts.</param>
/// <param name="builder">Secure request/response builder.</param>
/// <param name="logger">Logger instance.</param>
public sealed partial class KeyExchangeGrpcService(
    IServiceScopeFactory scopeFactory,
    SecureResponseBuilder builder,
    ILogger<KeyExchangeGrpcService> logger
) : KeyExchange.KeyExchangeBase {

    /// <summary>
    /// Returns the pending key rotation for the requesting agent, if any.
    /// The new AES-256 key is RSA-encrypted with the agent's public key for defense-in-depth.
    /// </summary>
    public override async Task<EncryptedEnvelope> FetchPendingKey(
        EncryptedEnvelope request,
        ServerCallContext context
    ) {
        (RegisteredConnection connection, FetchPendingKeyRequest inner) =
            SecureResponseBuilder.DecryptRequest<FetchPendingKeyRequest>( request, context );

        CancellationToken ct = context.CancellationToken;

        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext dbContext = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

        RegisteredConnection? agent = await dbContext.RegisteredConnections
            .FirstOrDefaultAsync( c => c.Id == connection.Id && c.IsServer, ct );

        if (agent is null) {
            throw new RpcException( new Status( StatusCode.NotFound,
                "Agent connection not found." ) );
        }

        FetchPendingKeyResponse response;

        if (agent.PendingSharedKey is not null && agent.PendingKeyId is not null) {
            // RSA-encrypt the pending key with the agent's public key (defense-in-depth)
            using RSA rsa = RSA.Create( );
            rsa.ImportParameters( agent.RemotePublicKey );
            byte[] rsaEncrypted = rsa.Encrypt( agent.PendingSharedKey, RSAEncryptionPadding.OaepSHA512 );

            response = new FetchPendingKeyResponse {
                HasPendingKey = true,
                RsaEncryptedNewKey = ByteString.CopyFrom( rsaEncrypted ),
                NewKeyId = agent.PendingKeyId,
            };

            LogPendingKeyFetched( logger, agent.Id, agent.PendingKeyId );
        } else {
            response = new FetchPendingKeyResponse {
                HasPendingKey = false,
            };
        }

        return await builder.EncryptResponseAsync( response, connection, ct );
    }

    /// <summary>
    /// Acknowledges that the agent has activated the pending key. Swaps the active key
    /// with the pending key and retains the previous key for the grace period.
    /// </summary>
    public override async Task<EncryptedEnvelope> AcknowledgeKey(
        EncryptedEnvelope request,
        ServerCallContext context
    ) {
        (RegisteredConnection connection, AcknowledgeKeyRequest inner) =
            SecureResponseBuilder.DecryptRequest<AcknowledgeKeyRequest>( request, context );

        CancellationToken ct = context.CancellationToken;

        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext dbContext = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

        RegisteredConnection? agent = await dbContext.RegisteredConnections
            .FirstOrDefaultAsync( c => c.Id == connection.Id && c.IsServer, ct );

        if (agent is null) {
            throw new RpcException( new Status( StatusCode.NotFound,
                "Agent connection not found." ) );
        }

        // Verify the activated key ID matches the pending key
        if (inner.ActivatedKeyId != agent.PendingKeyId) {
            LogKeyIdMismatch( logger, agent.Id, inner.ActivatedKeyId, agent.PendingKeyId );

            AcknowledgeKeyResponse mismatchResponse = new( ) { Success = false };
            return await builder.EncryptResponseAsync( mismatchResponse, connection, ct );
        }

        // Swap keys: current -> previous, pending -> current
        agent.PreviousSharedKey = agent.SharedKey;
        agent.PreviousKeyId = agent.ActiveKeyId;
        agent.SharedKey = agent.PendingSharedKey!;
        agent.ActiveKeyId = agent.PendingKeyId;
        agent.KeyRotatedAtUtc = DateTime.UtcNow;
        agent.PendingSharedKey = null;
        agent.PendingKeyId = null;

        _ = await dbContext.SaveChangesAsync( ct );

        LogKeyAcknowledged( logger, agent.Id, agent.ActiveKeyId! );

        AcknowledgeKeyResponse response = new( ) { Success = true };
        return await builder.EncryptResponseAsync( response, connection, ct );
    }

    [LoggerMessage( Level = LogLevel.Information,
        Message = "Agent {AgentId} fetched pending key rotation. PendingKeyId={PendingKeyId}" )]
    private static partial void LogPendingKeyFetched( ILogger logger, Guid agentId, string pendingKeyId );

    [LoggerMessage( Level = LogLevel.Warning,
        Message = "Agent {AgentId} acknowledged key ID {ActivatedKeyId} but pending key ID is {PendingKeyId}" )]
    private static partial void LogKeyIdMismatch( ILogger logger, Guid agentId, string activatedKeyId, string? pendingKeyId );

    [LoggerMessage( Level = LogLevel.Information,
        Message = "Agent {AgentId} acknowledged key rotation. ActiveKeyId={ActiveKeyId}" )]
    private static partial void LogKeyAcknowledged( ILogger logger, Guid agentId, string activeKeyId );
}
