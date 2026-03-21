using System.Net.Http.Headers;

namespace Werkr.Server.Identity;

/// <summary>
/// Delegating handler for background/system HTTP clients that always attaches
/// a service-identity JWT. Unlike <see cref="AuthForwardingHandler"/>, this
/// handler ignores <see cref="UserTokenContext"/> and is used exclusively for
/// system-initiated calls (health monitoring, SSE relay, etc.).
/// </summary>
public sealed partial class ServiceAuthForwardingHandler(
    JwtTokenService tokenService,
    ILogger<ServiceAuthForwardingHandler> logger
) : DelegatingHandler {
    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken
    ) {
        string token = tokenService.GenerateServiceToken( );
        request.Headers.Authorization = new AuthenticationHeaderValue( "Bearer", token );

        if (logger.IsEnabled( LogLevel.Debug )) {
            LogServiceJwtAttached( logger );
        }

        return base.SendAsync( request, cancellationToken );
    }

    [LoggerMessage( Level = LogLevel.Debug,
        Message = "Attached service JWT to outgoing system API request." )]
    private static partial void LogServiceJwtAttached( ILogger logger );
}
