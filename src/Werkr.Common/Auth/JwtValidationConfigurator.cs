using System.Text;

using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Werkr.Common.Auth;

/// <summary>
/// Shared JWT validation configuration used by both the API (JWT Bearer validation)
/// and the Server (if needed for token introspection). Reads signing key, issuer,
/// and audience from <c>Jwt:*</c> configuration keys (Decision A6).
/// </summary>
public static class JwtValidationConfigurator {
    /// <summary>
    /// Returns <see cref="TokenValidationParameters"/> built from the given configuration.
    /// </summary>
    /// <param name="config">The application configuration.</param>
    /// <returns>Configured <see cref="TokenValidationParameters"/>.</returns>
    public static TokenValidationParameters GetParameters( IConfiguration config ) {
        string signingKey = config["Jwt:SigningKey"]
            ?? throw new InvalidOperationException(
                "JWT signing key is not configured. Set 'Jwt:SigningKey' in appsettings.json or environment variables." );

        return new TokenValidationParameters {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey( Encoding.UTF8.GetBytes( signingKey ) ),
            ValidateIssuer = true,
            ValidIssuer = config["Jwt:Issuer"] ?? "werkr-api",
            ValidateAudience = true,
            ValidAudience = config["Jwt:Audience"] ?? "werkr",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes( 1 ),
        };
    }
}
