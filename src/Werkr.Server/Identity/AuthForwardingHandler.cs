using System.Net.Http.Headers;

namespace Werkr.Server.Identity;

/// <summary>
/// Delegating handler that attaches a self-minted JWT bearer token to
/// outgoing API requests from the Blazor Server. The Server is the sole
/// JWT issuer and trusts itself - no HTTP round-trip is needed (Decision A1).
/// </summary>
/// <remarks>
/// Initializes the auth forwarding handler.
/// </remarks>
public sealed partial class AuthForwardingHandler(
    JwtTokenService tokenService,
    ILogger<AuthForwardingHandler> logger
    ) : DelegatingHandler {
    /// <summary>
    /// The <see cref="JwtTokenService"/> used to mint short-lived service JWTs containing full admin-level permissions.
    /// </summary>
    private readonly JwtTokenService _tokenService = tokenService;
    /// <summary>
    /// Logger for diagnostic messages about outgoing authenticated requests.
    /// </summary>
    private readonly ILogger<AuthForwardingHandler> _logger = logger;

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken
    ) {
        string token = _tokenService.GenerateServiceToken( );
        request.Headers.Authorization = new AuthenticationHeaderValue( "Bearer", token );

        if (_logger.IsEnabled( LogLevel.Debug )) {
            _logger.LogDebug( "Attached self-minted service JWT to outgoing API request." );
        }

        return base.SendAsync( request, cancellationToken );
    }
}
