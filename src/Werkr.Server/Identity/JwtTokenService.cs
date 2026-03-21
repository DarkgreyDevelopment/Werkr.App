using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Werkr.Common.Auth;
using Werkr.Data.Identity.Entities;

namespace Werkr.Server.Identity;

/// <summary>
/// Service for generating short-lived JWT bearer tokens from validated API keys.
/// Uses HMAC-SHA256 symmetric signing. Tokens are 15-minute, non-refreshable.
/// <para>
/// Also generates service tokens for Server→API calls (no API key required)
/// via <see cref="GenerateServiceToken"/>.
/// </para>
/// </summary>
public sealed partial class JwtTokenService {
    /// <summary>
    /// The HMAC-SHA256 symmetric signing key derived from the <c>Jwt:SigningKey</c> configuration value. Must be at least 32 characters (256 bits).
    /// </summary>
    private readonly SymmetricSecurityKey _signingKey;
    /// <summary>
    /// The <c>iss</c> (issuer) claim value embedded in every generated token. Defaults to <c>"werkr-api"</c> when not configured.
    /// </summary>
    private readonly string _issuer;
    /// <summary>
    /// The <c>aud</c> (audience) claim value embedded in every generated token. Defaults to <c>"werkr"</c> when not configured.
    /// </summary>
    private readonly string _audience;
    /// <summary>
    /// The lifetime applied to every minted token. Defaults to 15 minutes when the <c>Jwt:TokenLifetimeMinutes</c> configuration key is absent or unparseable.
    /// </summary>
    private readonly TimeSpan _tokenLifetime;
    /// <summary>
    /// Logger for recording debug-level details about generated tokens.
    /// </summary>
    private readonly ILogger<JwtTokenService> _logger;

    /// <summary>
    /// Initializes the JWT token service with signing configuration.
    /// </summary>
    /// <param name="configuration">Application configuration (reads <c>Jwt:SigningKey</c>, <c>Jwt:Issuer</c>, <c>Jwt:Audience</c>).</param>
    /// <param name="logger">Logger instance.</param>
    public JwtTokenService( IConfiguration configuration, ILogger<JwtTokenService> logger ) {
        _logger = logger;

        string signingKey = configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException(
                "JWT signing key is not configured. Set 'Jwt:SigningKey' in appsettings.json or environment variables." );

        if (signingKey.Length < 32) {
            throw new InvalidOperationException(
                "JWT signing key must be at least 32 characters (256 bits) for HMAC-SHA256." );
        }

        _signingKey = new SymmetricSecurityKey( Encoding.UTF8.GetBytes( signingKey ) );
        _issuer = configuration["Jwt:Issuer"] ?? "werkr-api";
        _audience = configuration["Jwt:Audience"] ?? "werkr";
        _tokenLifetime = TimeSpan.FromMinutes(
            int.TryParse( configuration["Jwt:TokenLifetimeMinutes"], out int minutes ) ? minutes : 15 );
    }

    /// <summary>
    /// Generates a JWT bearer token for the given API key.
    /// </summary>
    /// <param name="apiKey">The validated API key entity.</param>
    /// <returns>The signed JWT token string.</returns>
    public string GenerateToken( ApiKey apiKey ) =>
        GenerateToken( apiKey, [] );

    /// <summary>
    /// Generates a JWT bearer token for the given API key with embedded permission claims.
    /// </summary>
    /// <param name="apiKey">The validated API key entity.</param>
    /// <param name="permissions">Pre-resolved permissions for the API key's role.</param>
    /// <returns>The signed JWT token string.</returns>
    public string GenerateToken( ApiKey apiKey, IReadOnlyList<Permission> permissions ) {
        List<Claim> claims = [
            new( ClaimTypes.NameIdentifier, apiKey.CreatedByUserId ),
            new( ClaimTypes.Role, apiKey.Role ),
            new( WerkrClaimTypes.ApiKeyId, apiKey.Id.ToString( ) ),
            new( WerkrClaimTypes.ApiKeyName, apiKey.Name ),
            new( WerkrClaimTypes.TokenOrigin, "api-key" ),
            new( JwtRegisteredClaimNames.Jti, Guid.NewGuid( ).ToString( ) ),
        ];

        foreach (Permission permission in permissions) {
            claims.Add( new Claim( WerkrClaimTypes.Permission, permission.ToString( ) ) );
        }

        string tokenString = MintToken( claims );

        if (_logger.IsEnabled( LogLevel.Debug )) {
            _logger.LogDebug( "Generated JWT for API key '{Name}' (prefix: {Prefix}), expires in {Lifetime} minutes, permissions: {Permissions}.",
                apiKey.Name, apiKey.KeyPrefix, _tokenLifetime.TotalMinutes,
                string.Join( ", ", permissions )
            );
        }

        return tokenString;
    }

    /// <summary>
    /// Mints a JWT for Server→API service-to-service calls.
    /// Uses a built-in service identity with full permissions.
    /// No API key required - the Server is the token issuer and trusts itself.
    /// </summary>
    /// <returns>The signed JWT token string.</returns>
    public string GenerateServiceToken( ) {
        List<Claim> claims = [
            new( ClaimTypes.NameIdentifier, "werkr-server" ),
            new( ClaimTypes.Role, "Admin" ),
            new( WerkrClaimTypes.ApiKeyId, Guid.Empty.ToString( ) ),
            new( WerkrClaimTypes.ApiKeyName, "werkr-server-internal" ),
            new( WerkrClaimTypes.TokenOrigin, "service" ),
            new( JwtRegisteredClaimNames.Jti, Guid.NewGuid( ).ToString( ) ),
        ];

        // Grant all permissions for service-to-service calls
        foreach (Permission permission in Enum.GetValues<Permission>( )) {
            claims.Add( new Claim( WerkrClaimTypes.Permission, permission.ToString( ) ) );
        }

        string tokenString = MintToken( claims );

        if (_logger.IsEnabled( LogLevel.Debug )) {
            _logger.LogDebug( "Generated Server service JWT, expires in {Lifetime} minutes.", _tokenLifetime.TotalMinutes );
        }

        return tokenString;
    }

    /// <summary>
    /// Mints a JWT carrying a real user's identity, roles, and resolved permissions.
    /// Used by <see cref="BlazorUserTokenProvider"/> to forward user context to the API.
    /// </summary>
    /// <param name="userId">The user's identity ID (NameIdentifier claim).</param>
    /// <param name="userName">The user's display name (Name claim).</param>
    /// <param name="roles">The user's assigned roles.</param>
    /// <param name="permissions">The user's resolved permissions from the role-permission mapping.</param>
    /// <returns>The signed JWT token string.</returns>
    public string GenerateUserToken(
        string userId,
        string userName,
        IEnumerable<string> roles,
        IReadOnlySet<Permission> permissions
    ) {
        List<Claim> claims = [
            new( ClaimTypes.NameIdentifier, userId ),
            new( ClaimTypes.Name, userName ),
            new( WerkrClaimTypes.TokenOrigin, "user-forwarded" ),
            new( JwtRegisteredClaimNames.Jti, Guid.NewGuid( ).ToString( ) ),
        ];

        foreach (string role in roles) {
            claims.Add( new Claim( ClaimTypes.Role, role ) );
        }

        foreach (Permission permission in permissions) {
            claims.Add( new Claim( WerkrClaimTypes.Permission, permission.ToString( ) ) );
        }

        string tokenString = MintToken( claims );

        if (_logger.IsEnabled( LogLevel.Debug )) {
            _logger.LogDebug(
                "Generated user-forwarded JWT for '{UserName}' ({UserId}), expires in {Lifetime} minutes, permissions: {Permissions}.",
                userName, userId, _tokenLifetime.TotalMinutes,
                string.Join( ", ", permissions )
            );
        }

        return tokenString;
    }

    /// <summary>
    /// Mints a signed JWT from the given claims using <see cref="JsonWebTokenHandler"/>.
    /// </summary>
    private string MintToken( List<Claim> claims ) {
        SigningCredentials credentials = new( _signingKey, SecurityAlgorithms.HmacSha256 );

        SecurityTokenDescriptor descriptor = new( ) {
            Issuer = _issuer,
            Audience = _audience,
            Subject = new ClaimsIdentity( claims ),
            Expires = DateTime.UtcNow.Add( _tokenLifetime ),
            SigningCredentials = credentials,
        };

        return new JsonWebTokenHandler( ).CreateToken( descriptor );
    }

    /// <summary>
    /// Gets the token validation parameters for configuring JWT Bearer authentication.
    /// </summary>
    /// <returns>The <see cref="TokenValidationParameters"/>.</returns>
    public TokenValidationParameters GetValidationParameters( ) {
        return new TokenValidationParameters {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signingKey,
            ValidateIssuer = true,
            ValidIssuer = _issuer,
            ValidateAudience = true,
            ValidAudience = _audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes( 1 ),
        };
    }
}
