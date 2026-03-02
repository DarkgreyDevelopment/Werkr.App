using System.Security.Cryptography;
using System.Text;

using Grpc.Core;
using Grpc.Core.Interceptors;

using Microsoft.EntityFrameworkCore;

using Werkr.Common.Models;
using Werkr.Core.Cryptography;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Api.Interceptors;

/// <summary>
/// gRPC server interceptor that validates Agent bearer tokens on incoming calls.
/// Looks up the <see cref="RegisteredConnection"/> by the <c>x-werkr-connection-id</c>
/// header and verifies the bearer token against the stored <see cref="RegisteredConnection.InboundApiKeyHash"/>.
/// <para>
/// If no <c>x-werkr-connection-id</c> header is present the call is assumed to be a
/// user/service call authenticated via JWT and the interceptor is a no-op.
/// </para>
/// </summary>
public class AgentBearerTokenInterceptor(
    IServiceScopeFactory scopeFactory,
    ILogger<AgentBearerTokenInterceptor> logger
) : Interceptor {

    /// <inheritdoc/>
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation ) {
        await ValidateBearerTokenAsync( context );
        return await continuation( request, context );
    }

    /// <inheritdoc/>
    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation ) {
        await ValidateBearerTokenAsync( context );
        await continuation( request, responseStream, context );
    }

    /// <inheritdoc/>
    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation ) {
        await ValidateBearerTokenAsync( context );
        return await continuation( requestStream, context );
    }

    /// <inheritdoc/>
    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation ) {
        await ValidateBearerTokenAsync( context );
        await continuation( requestStream, responseStream, context );
    }

    private async Task ValidateBearerTokenAsync( ServerCallContext context ) {
        // If no connection-id header is present, this is a user/service call — let JWT handle it.
        string? connectionIdStr = context.RequestHeaders.GetValue( "x-werkr-connection-id" );
        if (string.IsNullOrEmpty( connectionIdStr )) {
            return;
        }

        if (!Guid.TryParse( connectionIdStr, out Guid connectionId )) {
            throw new RpcException( new Status( StatusCode.Unauthenticated,
                "Invalid x-werkr-connection-id header." ) );
        }

        // Extract bearer token
        string? authHeader = context.RequestHeaders.GetValue( "authorization" );
        if (string.IsNullOrEmpty( authHeader ) ||
            !authHeader.StartsWith( "Bearer ", StringComparison.OrdinalIgnoreCase )) {
            throw new RpcException( new Status( StatusCode.Unauthenticated,
                "Missing or malformed authorization header." ) );
        }

        string token = authHeader["Bearer ".Length..];

        // Resolve the Server-side connection record from the app database
        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext dbContext = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

        RegisteredConnection? connection = await dbContext.RegisteredConnections
            .FirstOrDefaultAsync( c => c.Id == connectionId && c.IsServer );

        if (connection is null || connection.Status == ConnectionStatus.Revoked) {
            logger.LogWarning( "Agent gRPC call rejected: connection {ConnectionId} not found or revoked.",
                connectionId );
            throw new RpcException( new Status( StatusCode.Unauthenticated,
                "Connection not found or revoked." ) );
        }

        // Constant-time token comparison (SHA-512 hash then fixed-time equals)
        string tokenHash = EncryptionProvider.HashSHA512String( token );
        byte[] receivedBytes = Encoding.UTF8.GetBytes( tokenHash );
        byte[] storedBytes = Encoding.UTF8.GetBytes( connection.InboundApiKeyHash );

        if (!CryptographicOperations.FixedTimeEquals( receivedBytes, storedBytes )) {
            logger.LogWarning( "Agent gRPC call rejected: invalid bearer token for connection {ConnectionId}.",
                connectionId );
            throw new RpcException( new Status( StatusCode.Unauthenticated,
                "Invalid bearer token." ) );
        }

        // Debounced LastSeen update (only write if null or older than 60 seconds)
        if (connection.LastSeen is null || connection.LastSeen < DateTime.UtcNow.AddSeconds( -60 )) {
            connection.LastSeen = DateTime.UtcNow;
            _ = await dbContext.SaveChangesAsync( );
        }

        // Store resolved connection and optional call ID in UserState for downstream services
        context.UserState["Connection"] = connection;

        string? callId = context.RequestHeaders.GetValue( "x-werkr-call-id" );
        if (!string.IsNullOrEmpty( callId )) {
            context.UserState["CallId"] = callId;
        }
    }
}
