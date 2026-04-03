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
public partial class AgentBearerTokenInterceptor(
    IServiceScopeFactory scopeFactory,
    ILogger<AgentBearerTokenInterceptor> logger
) : Interceptor {

    /// <inheritdoc/>
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation
    ) {
        await ValidateBearerTokenAsync( context );
        return await continuation( request, context );
    }

    /// <inheritdoc/>
    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation
    ) {
        await ValidateBearerTokenAsync( context );
        await continuation( request, responseStream, context );
    }

    /// <inheritdoc/>
    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation
    ) {
        await ValidateBearerTokenAsync( context );
        return await continuation( requestStream, context );
    }

    /// <inheritdoc/>
    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation
    ) {
        await ValidateBearerTokenAsync( context );
        await continuation( requestStream, responseStream, context );
    }

    /// <summary>
    /// Validates the bearer token and connection ID metadata headers on inbound gRPC calls.
    /// For registration calls (identified by <c>x-werkr-bundle-id</c> header), looks up the
    /// <see cref="RegistrationBundle"/> and stores the registration key in context for the service.
    /// </summary>
    private async Task ValidateBearerTokenAsync( ServerCallContext context ) {
        // Registration path: x-werkr-bundle-id present but no x-werkr-connection-id.
        // Look up the bundle and store its RegistrationKey for the gRPC service to decrypt.
        string? bundleIdHex = context.RequestHeaders.GetValue( "x-werkr-bundle-id" );
        string? connectionIdStr = context.RequestHeaders.GetValue( "x-werkr-connection-id" );

        if (!string.IsNullOrEmpty( bundleIdHex ) && string.IsNullOrEmpty( connectionIdStr )) {
            await ResolveRegistrationBundleAsync( context, bundleIdHex );
            return;
        }

        // If no connection-id header is present, this is a user/service call — let JWT handle it.
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
            try {
                _ = await dbContext.SaveChangesAsync( );
            } catch (DbUpdateConcurrencyException) {
                // Best-effort write — another request already updated LastSeen.
                if (logger.IsEnabled( LogLevel.Debug )) {
                    logger.LogDebug( "Swallowed DbUpdateConcurrencyException on LastSeen update for connection {ConnectionId}.",
                        connectionId );
                }
            }
        }

        // Store resolved connection and optional call ID in UserState for downstream services
        context.UserState["Connection"] = connection;

        string? callId = context.RequestHeaders.GetValue( "x-werkr-call-id" );
        if (!string.IsNullOrEmpty( callId )) {
            context.UserState["CallId"] = callId;
        }
    }

    /// <summary>
    /// Resolves a <see cref="RegistrationBundle"/> by its hex-encoded BundleId header.
    /// Stores the bundle's <c>RegistrationKey</c> and the bundle entity itself in
    /// <see cref="ServerCallContext.UserState"/> so the registration gRPC service can
    /// decrypt the request and encrypt the response.
    /// </summary>
    private async Task ResolveRegistrationBundleAsync( ServerCallContext context, string bundleIdHex ) {
        byte[] bundleId;
        try {
            bundleId = Convert.FromHexString( bundleIdHex );
        } catch (FormatException) {
            throw new RpcException( new Status( StatusCode.Unauthenticated,
                "Invalid x-werkr-bundle-id header." ) );
        }

        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext dbContext = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

        RegistrationBundle? bundle = await dbContext.RegistrationBundles
            .FirstOrDefaultAsync( b => b.BundleId == bundleId );

        if (bundle is null) {
            logger.LogWarning( "Registration call rejected: unknown bundle ID." );
            throw new RpcException( new Status( StatusCode.Unauthenticated,
                "Unknown registration bundle." ) );
        }

        if (bundle.RegistrationKey is null) {
            logger.LogWarning( "Registration call rejected: bundle has no registration key (already consumed?)." );
            throw new RpcException( new Status( StatusCode.Unauthenticated,
                "Registration bundle key unavailable." ) );
        }

        context.UserState["RegistrationKey"] = bundle.RegistrationKey;
        context.UserState["RegistrationBundle"] = bundle;
    }
}
