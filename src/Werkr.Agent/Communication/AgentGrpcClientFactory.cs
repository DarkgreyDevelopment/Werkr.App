using Grpc.Core;
using Grpc.Net.Client;

using Microsoft.EntityFrameworkCore;

using Werkr.Common.Models;
using Werkr.Common.Protos;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Agent.Communication;

/// <summary>
/// Manages the Agent's outbound gRPC channel to the Server.
/// On first use, reads the <see cref="RegisteredConnection"/> (where <c>IsServer == false</c>)
/// from the Agent's local SQLite database. Creates and caches a <see cref="GrpcChannel"/>
/// to the Server's <see cref="RegisteredConnection.RemoteUrl"/>.
/// Provides typed client accessors and <see cref="CallOptions"/> with bearer token authentication.
/// </summary>
/// <param name="scopeFactory">Factory for creating DI scopes to resolve <see cref="WerkrDbContext"/>.</param>
/// <param name="logger">Logger instance.</param>
public sealed class AgentGrpcClientFactory(
    IServiceScopeFactory scopeFactory,
    ILogger<AgentGrpcClientFactory> logger
) : IDisposable {
    private GrpcChannel? _channel;
    private RegisteredConnection? _connection;
    private readonly SemaphoreSlim _initLock = new( 1, 1 );

    /// <summary>
    /// Gets the cached <see cref="RegisteredConnection"/> for the Server.
    /// Initializes on first call.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The Agent's registration connection record.</returns>
    /// <exception cref="InvalidOperationException">No registration found.</exception>
    public async Task<RegisteredConnection> GetConnectionAsync( CancellationToken ct = default ) {
        await EnsureInitializedAsync( ct );
        return _connection!;
    }

    /// <summary>
    /// Creates a <see cref="ScheduleSync.ScheduleSyncClient"/> for pulling schedules from the Server.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A configured gRPC client.</returns>
    public async Task<ScheduleSync.ScheduleSyncClient> CreateScheduleSyncClientAsync( CancellationToken ct = default ) {
        await EnsureInitializedAsync( ct );
        return new ScheduleSync.ScheduleSyncClient( _channel );
    }

    /// <summary>
    /// Creates a <see cref="JobReporting.JobReportingClient"/> for reporting job results to the Server.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A configured gRPC client.</returns>
    public async Task<JobReporting.JobReportingClient> CreateJobReportingClientAsync( CancellationToken ct = default ) {
        await EnsureInitializedAsync( ct );
        return new JobReporting.JobReportingClient( _channel );
    }

    /// <summary>
    /// Creates a <see cref="WorkflowExecution.WorkflowExecutionClient"/> for requesting workflow execution on the Server.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A configured gRPC client.</returns>
    public async Task<WorkflowExecution.WorkflowExecutionClient> CreateWorkflowExecutionClientAsync( CancellationToken ct = default ) {
        await EnsureInitializedAsync( ct );
        return new WorkflowExecution.WorkflowExecutionClient( _channel );
    }

    /// <summary>
    /// Creates gRPC <see cref="CallOptions"/> with bearer token, connection ID, call ID, and deadline.
    /// Mirrors the pattern from <c>AgentConnectionManager.CreateCallOptions</c>.
    /// </summary>
    /// <param name="callId">Optional call ID for tracing. Generated if null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="timeout">Call timeout. Defaults to 5 minutes if null.</param>
    /// <returns>Configured <see cref="CallOptions"/>.</returns>
    public CallOptions CreateCallOptions(
        Guid? callId = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null ) {

        if (_connection is null) {
            throw new InvalidOperationException( "AgentGrpcClientFactory has not been initialized. Call any Create*Client method first." );
        }

        Metadata metadata = new( ) {
            { "authorization", $"Bearer {_connection.OutboundApiKey}" },
            { "x-werkr-connection-id", _connection.Id.ToString( ) },
            { "x-werkr-call-id", ( callId ?? Guid.NewGuid( ) ).ToString( ) }
        };

        TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromMinutes( 5 );
        DateTime deadline = DateTime.UtcNow + effectiveTimeout;

        return new CallOptions(
            headers: metadata,
            deadline: deadline,
            cancellationToken: cancellationToken );
    }

    /// <summary>
    /// Gets the SharedKey for encrypting outbound payloads.
    /// Must be called after initialization (any Create*Client call or GetConnectionAsync).
    /// </summary>
    /// <returns>The 32-byte AES-256 shared key.</returns>
    /// <exception cref="InvalidOperationException">Thrown when not yet initialized.</exception>
    public byte[] GetSharedKey( ) {
        return _connection?.SharedKey
            ?? throw new InvalidOperationException(
                "AgentGrpcClientFactory has not been initialized. Call any Create*Client method first." );
    }

    /// <summary>
    /// Gets the active key identifier for envelope encryption.
    /// Falls back to the connection ID if no rotation has occurred.
    /// Must be called after initialization.
    /// </summary>
    /// <returns>The active key ID string.</returns>
    /// <exception cref="InvalidOperationException">Thrown when not yet initialized.</exception>
    public string GetKeyId( ) {
        return _connection is null
            ? throw new InvalidOperationException(
                "AgentGrpcClientFactory has not been initialized. Call any Create*Client method first." )
            : _connection.ActiveKeyId ?? _connection.Id.ToString( );
    }

    /// <summary>
    /// Returns whether the Agent has been registered (has a connection to the Server).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if a valid registration exists.</returns>
    public async Task<bool> IsRegisteredAsync( CancellationToken ct = default ) {
        try {
            await EnsureInitializedAsync( ct );
            return _connection is not null;
        } catch (InvalidOperationException) {
            return false;
        }
    }

    /// <summary>
    /// Resets the cached channel and connection, forcing re-initialization on next use.
    /// Call when the connection details may have changed (e.g., after re-registration).
    /// </summary>
    public void Reset( ) {
        _channel?.Dispose( );
        _channel = null;
        _connection = null;
    }

    /// <inheritdoc/>
    public void Dispose( ) {
        _channel?.Dispose( );
        _initLock.Dispose( );
    }

    /// <summary>
    /// Ensures the gRPC channel and connection are initialized.
    /// Thread-safe via <see cref="SemaphoreSlim"/>.
    /// </summary>
    private async Task EnsureInitializedAsync( CancellationToken ct ) {
        if (_channel is not null && _connection is not null && _channel.State != ConnectivityState.Shutdown) {
            return;
        }

        await _initLock.WaitAsync( ct );
        try {
            // Double-check after acquiring lock
            if (_channel is not null && _connection is not null && _channel.State != ConnectivityState.Shutdown) {
                return;
            }

            _connection = await ResolveConnectionAsync( ct );

            _channel?.Dispose( );
            _channel = GrpcChannel.ForAddress( _connection.RemoteUrl, new GrpcChannelOptions {
                HttpHandler = CreateHttpHandler( )
            } );

            if (logger.IsEnabled( LogLevel.Information )) {
                logger.LogInformation(
                    "Created gRPC channel to Server at {Url} (Connection: {ConnectionId}).",
                    _connection.RemoteUrl, _connection.Id.ToString( ) );
            }
        } finally {
            _ = _initLock.Release( );
        }
    }

    /// <summary>
    /// Resolves the Agent's <see cref="RegisteredConnection"/> from the local SQLite database.
    /// The Agent side has <c>IsServer == false</c>.
    /// </summary>
    private async Task<RegisteredConnection> ResolveConnectionAsync( CancellationToken ct ) {
        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext dbContext = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

        RegisteredConnection? connection = await dbContext.RegisteredConnections
            .AsNoTracking( )
            .FirstOrDefaultAsync( c => !c.IsServer && c.Status == ConnectionStatus.Connected, ct );

        return connection ?? throw new InvalidOperationException(
            "No registered server connection found. The agent must complete registration before scheduling can begin." );
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
}
