using System.Collections.Concurrent;
using Werkr.Common.Models;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Core.Communication;

/// <summary>
/// Manages gRPC channels to registered Agents. Caches channels in a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by connection ID.
/// Registered as a Singleton; queries <see cref="WerkrDbContext"/> via
/// <see cref="IServiceScopeFactory"/> to resolve connection details.
/// </summary>
/// <remarks>Initializes a new instance of the <see cref="AgentConnectionManager"/> class.</remarks>
/// <param name="scopeFactory">Factory for creating DI scopes to resolve scoped services.</param>
/// <param name="logger">Logger instance.</param>
public sealed class AgentConnectionManager(
    IServiceScopeFactory scopeFactory,
    ILogger<AgentConnectionManager> logger
) : IDisposable {
    private readonly ConcurrentDictionary<Guid, GrpcChannel> _channels = new( );

    /// <summary>
    /// Gets or creates a gRPC channel to the specified agent.
    /// Returns the channel along with the connection details needed for call credentials.
    /// </summary>
    /// <param name="agentConnectionId">The <see cref="RegisteredConnection.Id"/> for the Server-side record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple of the <see cref="GrpcChannel"/> and the <see cref="RegisteredConnection"/>.</returns>
    public async Task<(GrpcChannel Channel, RegisteredConnection Connection)> GetChannelAsync(
        Guid agentConnectionId,
        CancellationToken cancellationToken = default
    ) {

        // Try cache first
        if (_channels.TryGetValue(
            agentConnectionId,
            out GrpcChannel? cached
        ) &&
            cached.State != ConnectivityState.Shutdown) {
            // Still need the connection record for credentials
            RegisteredConnection cachedConn = await ResolveConnectionAsync(
                agentConnectionId,
                cancellationToken
            );
            return (cached, cachedConn);
        }

        RegisteredConnection connection = await ResolveConnectionAsync(
            agentConnectionId,
            cancellationToken
        );

        GrpcChannel channel = GrpcChannel.ForAddress( connection.RemoteUrl, new GrpcChannelOptions {
            HttpHandler = CreateHttpHandler( )
        } );

        _ = _channels.AddOrUpdate( agentConnectionId, channel, ( _, old ) => {
            old.Dispose( );
            return channel;
        } );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Created gRPC channel to Agent {AgentId} at {Url}.",
                agentConnectionId.ToString( ),
                connection.RemoteUrl
            );
        }

        return (channel, connection);
    }

    /// <summary>
    /// Creates gRPC <see cref="CallOptions"/> with bearer token, connection ID, call ID, and deadline.
    /// </summary>
    /// <param name="connection">The resolved <see cref="RegisteredConnection"/>.</param>
    /// <param name="callId">Optional call ID for tracing. Generated if null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="timeout">Command timeout. Defaults to 30 minutes if null.</param>
    /// <returns>Configured <see cref="CallOptions"/>.</returns>
    public static CallOptions CreateCallOptions(
        RegisteredConnection connection,
        Guid? callId = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null
    ) {

        Metadata metadata = new( ) {
            { "authorization", $"Bearer {connection.OutboundApiKey}" },
            { "x-werkr-connection-id", connection.Id.ToString( ) },
            { "x-werkr-call-id", (callId ?? Guid.NewGuid( )).ToString( ) }
        };

        TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromMinutes( 30 );
        DateTime deadline = DateTime.UtcNow + effectiveTimeout;

        return new CallOptions(
            headers: metadata,
            deadline: deadline,
            cancellationToken: cancellationToken
        );
    }

    /// <summary>
    /// Removes and disposes the cached channel for a specific agent.
    /// Call when a connection is revoked or an agent becomes unreachable.
    /// </summary>
    /// <param name="agentConnectionId">The connection ID to remove.</param>
    public void RemoveChannel( Guid agentConnectionId ) {
        if (_channels.TryRemove(
            agentConnectionId,
            out GrpcChannel? channel
        )) {
            channel.Dispose( );
            if (logger.IsEnabled( LogLevel.Information )) {
                logger.LogInformation(
                    "Removed gRPC channel for Agent {AgentId}.",
                    agentConnectionId.ToString( )
                );
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose( ) {
        foreach (KeyValuePair<Guid, GrpcChannel> kvp in _channels) {
            kvp.Value.Dispose( );
        }
        _channels.Clear( );
    }

    /// <summary>
    /// Creates an <see cref="HttpMessageHandler"/> for gRPC channels.
    /// All connections use TLS — ALPN negotiates HTTP/2 automatically.
    /// </summary>
    private static SocketsHttpHandler CreateHttpHandler( ) => new( ) {
        EnableMultipleHttp2Connections = true,
        KeepAlivePingDelay = TimeSpan.FromSeconds( 30 ),
        KeepAlivePingTimeout = TimeSpan.FromSeconds( 10 ),
    };

    private async Task<RegisteredConnection> ResolveConnectionAsync(
        Guid agentConnectionId,
        CancellationToken cancellationToken
    ) {

        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext dbContext = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

        RegisteredConnection? connection = await dbContext.RegisteredConnections
            .AsNoTracking( )
            .FirstOrDefaultAsync(
                c => c.Id == agentConnectionId && c.IsServer,
                cancellationToken
            );

        return connection is null || connection.Status == ConnectionStatus.Revoked
            ? throw new InvalidOperationException(
                $"Agent connection '{agentConnectionId}' not found or revoked." )
            : connection;
    }
}
