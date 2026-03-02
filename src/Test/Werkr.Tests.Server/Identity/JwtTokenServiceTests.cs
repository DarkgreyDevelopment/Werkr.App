using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Werkr.Data.Identity.Entities;
using Werkr.Server.Identity;

namespace Werkr.Tests.Server.Identity;

/// <summary>
/// Unit tests for <see cref="JwtTokenService"/>.
/// </summary>
[TestClass]
public class JwtTokenServiceTests {
    private const string TestSigningKey = "test-signing-key-that-is-at-least-32-characters-long!";
    private const string TestIssuer = "werkr-test-issuer";
    private const string TestAudience = "werkr-test-audience";

    private static JwtTokenService CreateService( string? signingKey = null, string? issuer = null, string? audience = null ) {
        Dictionary<string, string?> config = new( ) {
            ["Jwt:SigningKey"] = signingKey ?? TestSigningKey,
            ["Jwt:Issuer"] = issuer ?? TestIssuer,
            ["Jwt:Audience"] = audience ?? TestAudience,
            ["Jwt:TokenLifetimeMinutes"] = "15",
        };

        IConfiguration configuration = new ConfigurationBuilder( )
            .AddInMemoryCollection( config )
            .Build( );

        return new JwtTokenService( configuration, NullLogger<JwtTokenService>.Instance );
    }

    private static ApiKey CreateTestApiKey( ) => new( ) {
        Id = Guid.NewGuid( ),
        KeyHash = "test-hash",
        KeyPrefix = "wk_test_1234",
        Name = "Test Key",
        Role = "Admin",
        CreatedByUserId = "user-123",
        CreatedUtc = DateTime.UtcNow,
    };

    [TestMethod]
    public void Constructor_ThrowsWhenSigningKeyMissing( ) {
        Dictionary<string, string?> config = new( ) {
            ["Jwt:Issuer"] = TestIssuer,
        };

        IConfiguration configuration = new ConfigurationBuilder( )
            .AddInMemoryCollection( config )
            .Build( );

        _ = Assert.ThrowsExactly<InvalidOperationException>(
            ( ) => new JwtTokenService( configuration, NullLogger<JwtTokenService>.Instance ) );
    }

    [TestMethod]
    public void Constructor_ThrowsWhenSigningKeyTooShort( ) {
        Dictionary<string, string?> config = new( ) {
            ["Jwt:SigningKey"] = "short",
            ["Jwt:Issuer"] = TestIssuer,
        };

        IConfiguration configuration = new ConfigurationBuilder( )
            .AddInMemoryCollection( config )
            .Build( );

        _ = Assert.ThrowsExactly<InvalidOperationException>(
            ( ) => new JwtTokenService( configuration, NullLogger<JwtTokenService>.Instance ) );
    }

    [TestMethod]
    public async Task GenerateToken_ProducesValidJwt( ) {
        JwtTokenService service = CreateService( );
        ApiKey apiKey = CreateTestApiKey( );

        string token = service.GenerateToken( apiKey );

        Assert.IsFalse( string.IsNullOrWhiteSpace( token ) );

        // Parse and validate the token
        JsonWebTokenHandler handler = new( );
        TokenValidationParameters tvp = service.GetValidationParameters( );

        TokenValidationResult result = await handler.ValidateTokenAsync( token, tvp );

        Assert.IsTrue( result.IsValid, $"Token validation failed: {result.Exception?.Message}" );
        Assert.IsNotNull( result.ClaimsIdentity );
    }

    [TestMethod]
    public void GenerateToken_ContainsExpectedClaims( ) {
        JwtTokenService service = CreateService( );
        ApiKey apiKey = CreateTestApiKey( );

        string token = service.GenerateToken( apiKey );

        JsonWebTokenHandler handler = new( );
        JsonWebToken jwt = handler.ReadJsonWebToken( token );

        Assert.AreEqual( apiKey.CreatedByUserId,
            jwt.Claims.First( c => c.Type is ClaimTypes.NameIdentifier
                or "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier" ).Value );
        Assert.AreEqual( apiKey.Role,
            jwt.Claims.First( c => c.Type is ClaimTypes.Role
                or "http://schemas.microsoft.com/ws/2008/06/identity/claims/role" ).Value );
        Assert.AreEqual( apiKey.Id.ToString( ),
            jwt.Claims.First( c => c.Type == "api_key_id" ).Value );
        Assert.AreEqual( apiKey.Name,
            jwt.Claims.First( c => c.Type == "api_key_name" ).Value );
        Assert.IsFalse( string.IsNullOrWhiteSpace(
            jwt.Claims.First( c => c.Type == JwtRegisteredClaimNames.Jti ).Value ) );
    }

    [TestMethod]
    public void GenerateToken_SetsCorrectIssuerAndAudience( ) {
        JwtTokenService service = CreateService( );
        ApiKey apiKey = CreateTestApiKey( );

        string token = service.GenerateToken( apiKey );
        JsonWebToken jwt = new JsonWebTokenHandler( ).ReadJsonWebToken( token );

        Assert.AreEqual( TestIssuer, jwt.Issuer );
        Assert.IsTrue( jwt.Audiences.Contains( TestAudience ) );
    }

    [TestMethod]
    public void GenerateToken_SetsExpirationInFuture( ) {
        JwtTokenService service = CreateService( );
        ApiKey apiKey = CreateTestApiKey( );

        string token = service.GenerateToken( apiKey );
        JsonWebToken jwt = new JsonWebTokenHandler( ).ReadJsonWebToken( token );

        Assert.IsGreaterThan( DateTime.UtcNow, jwt.ValidTo, "Token should expire in the future." );
        Assert.IsLessThan( DateTime.UtcNow.AddMinutes( 20 ), jwt.ValidTo, "Token should expire within 20 minutes." );
    }

    [TestMethod]
    public void GenerateToken_EachTokenHasUniqueJti( ) {
        JwtTokenService service = CreateService( );
        ApiKey apiKey = CreateTestApiKey( );

        string token1 = service.GenerateToken( apiKey );
        string token2 = service.GenerateToken( apiKey );

        JsonWebTokenHandler handler = new( );
        string jti1 = handler.ReadJsonWebToken( token1 ).Claims.First( c => c.Type == JwtRegisteredClaimNames.Jti ).Value;
        string jti2 = handler.ReadJsonWebToken( token2 ).Claims.First( c => c.Type == JwtRegisteredClaimNames.Jti ).Value;

        Assert.AreNotEqual( jti1, jti2, "Each token should have a unique JTI." );
    }

    [TestMethod]
    public void GetValidationParameters_ReturnsProperlyConfigure( ) {
        JwtTokenService service = CreateService( );

        TokenValidationParameters tvp = service.GetValidationParameters( );

        Assert.IsTrue( tvp.ValidateIssuerSigningKey );
        Assert.IsTrue( tvp.ValidateIssuer );
        Assert.IsTrue( tvp.ValidateAudience );
        Assert.IsTrue( tvp.ValidateLifetime );
        Assert.AreEqual( TestIssuer, tvp.ValidIssuer );
        Assert.AreEqual( TestAudience, tvp.ValidAudience );
        Assert.IsNotNull( tvp.IssuerSigningKey );
    }

    [TestMethod]
    public void GenerateToken_DefaultsUsedWhenConfigMissing( ) {
        Dictionary<string, string?> config = new( ) {
            ["Jwt:SigningKey"] = TestSigningKey,
            // Issuer and Audience not set — should use defaults
        };

        IConfiguration configuration = new ConfigurationBuilder( )
            .AddInMemoryCollection( config )
            .Build( );

        JwtTokenService service = new( configuration, NullLogger<JwtTokenService>.Instance );
        ApiKey apiKey = CreateTestApiKey( );

        string token = service.GenerateToken( apiKey );
        JsonWebToken jwt = new JsonWebTokenHandler( ).ReadJsonWebToken( token );

        Assert.AreEqual( "werkr-api", jwt.Issuer );
        Assert.IsTrue( jwt.Audiences.Contains( "werkr" ) );
    }
}
