using System.Security.Cryptography;
using System.Text;

using Grpc.Core;
using Grpc.Core.Interceptors;

using Microsoft.EntityFrameworkCore;

using Werkr.Common.Models;
using Werkr.Core.Cryptography;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Agent.Interceptors;

/// <summary>
/// gRPC server interceptor that validates bearer token authentication and connection ID
/// on all incoming calls. Resolves the <see cref="RegisteredConnection"/> and stores it
/// in <c>context.UserState</c> for downstream services.
/// </summary>
/// <remarks>Creates a new <see cref="BearerTokenInterceptor"/>.</remarks>
/// <param name="scopeFactory">Factory for creating DI scopes to resolve <see cref="WerkrDbContext"/>.</param>
/// <param name="logger">Logger for diagnostics.</param>
public class BearerTokenInterceptor(
    IServiceScopeFactory scopeFactory,
    ILogger<BearerTokenInterceptor> logger
) : Interceptor {

    /// <inheritdoc/>
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation
    ) {
        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "Received gRPC call to {Method} from {Peer}", context.Method, context.Peer );
        }

        await ValidateAndAttachConnectionAsync( context );
        return await continuation( request, context );
    }

    /// <inheritdoc/>
    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation
    ) {

        await ValidateAndAttachConnectionAsync( context );
        await continuation( request, responseStream, context );
    }

    private async Task ValidateAndAttachConnectionAsync( ServerCallContext context ) {
        // Extract authorization header
        string? authHeader = context.RequestHeaders.GetValue( "authorization" );
        if (string.IsNullOrEmpty( authHeader ) || !authHeader.StartsWith( "Bearer ", StringComparison.OrdinalIgnoreCase )) {
            throw new RpcException( new Status( StatusCode.Unauthenticated, "Missing authentication credentials." ) );
        }

        string token = authHeader["Bearer ".Length..];

        // Extract connection ID header
        string? connectionIdStr = context.RequestHeaders.GetValue( "x-werkr-connection-id" );
        if (string.IsNullOrEmpty( connectionIdStr ) || !Guid.TryParse( connectionIdStr, out Guid connectionId )) {
            throw new RpcException( new Status( StatusCode.Unauthenticated, "Missing authentication credentials." ) );
        }

        // Resolve connection from database
        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext dbContext = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

        RegisteredConnection? connection = await dbContext.RegisteredConnections
            .FirstOrDefaultAsync( c => c.Id == connectionId && !c.IsServer );

        if (connection is null || connection.Status == ConnectionStatus.Revoked) {
            throw new RpcException( new Status( StatusCode.Unauthenticated, "Connection not found or revoked." ) );
        }

        // Constant-time token comparison
        string tokenHash = EncryptionProvider.HashSHA512String( token );
        byte[] receivedBytes = Encoding.UTF8.GetBytes( tokenHash );
        byte[] storedBytes = Encoding.UTF8.GetBytes( connection.InboundApiKeyHash );

        if (!CryptographicOperations.FixedTimeEquals( receivedBytes, storedBytes )) {
            throw new RpcException( new Status( StatusCode.Unauthenticated, "Invalid bearer token." ) );
        }

        // Debounced LastSeen update (only write if null or older than 60 seconds)
        if (connection.LastSeen is null || connection.LastSeen < DateTime.UtcNow.AddSeconds( -60 )) {
            connection.LastSeen = DateTime.UtcNow;
            _ = await dbContext.SaveChangesAsync( );
        }

        // Store resolved connection in UserState for downstream services
        context.UserState["Connection"] = connection;

        // Store CallId if present
        string? callId = context.RequestHeaders.GetValue( "x-werkr-call-id" );
        if (!string.IsNullOrEmpty( callId )) {
            context.UserState["CallId"] = callId;
        }
    }
}
