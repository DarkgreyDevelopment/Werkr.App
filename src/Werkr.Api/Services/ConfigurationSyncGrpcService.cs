using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Werkr.Common.Models;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Core.Configuration;
using Werkr.Data;
using Werkr.Data.Entities.Configuration;
using Werkr.Data.Entities.Registration;

namespace Werkr.Api.Services;

/// <summary>
/// gRPC service for agent configuration synchronization.
/// Agents call <see cref="GetConfiguration"/> to pull effective configuration entries.
/// All RPCs use <see cref="EncryptedEnvelope"/>.
/// </summary>
/// <param name="dbContext">Database context.</param>
/// <param name="configService">Configuration resolution service.</param>
/// <param name="logger">Logger instance.</param>
public sealed partial class ConfigurationSyncGrpcService(
    WerkrDbContext dbContext,
    IConfigurationResolutionService configService,
    ILogger<ConfigurationSyncGrpcService> logger
) : Werkr.Common.Protos.ConfigurationSync.ConfigurationSyncBase {

    /// <summary>
    /// Returns configuration entries for the requesting agent.
    /// Supports delta sync via <c>last_known_version</c>.
    /// </summary>
    public override async Task<EncryptedEnvelope> GetConfiguration(
        EncryptedEnvelope request,
        ServerCallContext context
    ) {
        RegisteredConnection connection = GetConnection( context );
        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );

        ConfigSyncRequest inner = PayloadEncryptor.DecryptFromEnvelope<ConfigSyncRequest>(
            request, connection.SharedKey );

        if (string.IsNullOrWhiteSpace( inner.ConnectionId )) {
            throw new RpcException( new Status( StatusCode.InvalidArgument, "Connection ID is required." ) );
        }

        string agentId = connection.Id.ToString( );
        long sinceVersion = inner.LastKnownVersion;

        // Get delta or full config entries
        IReadOnlyList<ConfigurationEntryDto> entries = sinceVersion > 0
            ? await configService.GetDeltaAsync( sinceVersion, agentId, context.CancellationToken )
            : await configService.GetAllAsync( null, null, null, context.CancellationToken );

        long currentVersion = await configService.GetCurrentVersionAsync( context.CancellationToken );

        // Build response
        ConfigSyncResponse response = new( ) {
            CurrentVersion = currentVersion,
        };

        foreach (ConfigurationEntryDto entry in entries) {
            response.Entries.Add( new ConfigEntryMessage {
                Key = entry.Key,
                Value = entry.Value,
                ValueType = entry.ValueType,
                Category = entry.Category,
                Version = entry.SyncVersion,
                Deleted = false,
            } );
        }

        // Include credential metadata (not values)
        List<Credential> credentials = await dbContext.Credentials
            .AsNoTracking( )
            .Include( c => c.AgentScopes )
            .ToListAsync( context.CancellationToken );

        foreach (Credential cred in credentials) {
            bool isScoped = cred.AgentScopes.Count > 0
                && cred.AgentScopes.Any( s => s.AgentConnectionId == connection.Id );
            bool isAvailable = cred.AgentScopes.Count == 0 || isScoped;

            if (isAvailable) {
                response.Credentials.Add( new CredentialMetadata {
                    Name = cred.Name,
                    CredentialType = cred.Type.ToString( ),
                    IsScopedToThisAgent = isScoped,
                } );
            }
        }

        LogConfigSync( logger, agentId, sinceVersion, entries.Count );

        return PayloadEncryptor.EncryptToEnvelope( response, connection.SharedKey, keyId );
    }

    private static RegisteredConnection GetConnection( ServerCallContext context ) {
        return context.UserState.TryGetValue( "Connection", out object? connObj ) && connObj is RegisteredConnection connection
            ? connection
            : throw new RpcException( new Status( StatusCode.Internal, "Connection not resolved by interceptor." ) );
    }

    [LoggerMessage( Level = LogLevel.Debug, Message = "Config sync for agent {AgentId}: sinceVersion={SinceVersion}, returned {Count} entries" )]
    private static partial void LogConfigSync( ILogger logger, string agentId, long sinceVersion, int count );
}
