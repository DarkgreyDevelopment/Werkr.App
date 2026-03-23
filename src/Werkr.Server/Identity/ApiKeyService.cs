using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Werkr.Data.Identity;
using Werkr.Data.Identity.Entities;

namespace Werkr.Server.Identity;

/// <summary>
/// Service for creating, validating, and managing API keys.
/// API keys are stored as SHA-512 hashes; the raw key is only returned at creation time.
/// </summary>
public sealed partial class ApiKeyService( WerkrIdentityDbContext dbContext, ILogger<ApiKeyService> logger ) {

    /// <summary>
    /// Creates a new API key for the specified user with the given role.
    /// </summary>
    /// <param name="name">Human-readable name for the key.</param>
    /// <param name="role">The role to assign to tokens generated from this key.</param>
    /// <param name="createdByUserId">The user ID of the creator.</param>
    /// <param name="expiresUtc">Optional expiration date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A tuple of the created entity and the raw key (only available at creation time).</returns>
    public async Task<(ApiKey Entity, string RawKey)> CreateAsync(
        string name,
        string role,
        string createdByUserId,
        DateTime? expiresUtc = null,
        CancellationToken ct = default
    ) {
        // Generate a crypto-random 32-byte key, encoded as base64url
        byte[] keyBytes = RandomNumberGenerator.GetBytes( 32 );
        string rawKey = $"wk_{Convert.ToBase64String( keyBytes ).TrimEnd( '=' ).Replace( '+', '-' ).Replace( '/', '_' )}";

        string keyHash = ComputeHash( rawKey );
        string keyPrefix = rawKey[..Math.Min( 12, rawKey.Length )];

        ApiKey apiKey = new( ) {
            Id = Guid.NewGuid( ),
            KeyHash = keyHash,
            KeyPrefix = keyPrefix,
            Name = name.Trim( ),
            Role = role,
            CreatedByUserId = createdByUserId,
            CreatedUtc = DateTime.UtcNow,
            ExpiresUtc = expiresUtc,
            IsRevoked = false,
        };

        _ = dbContext.ApiKeys.Add( apiKey );
        _ = await dbContext.SaveChangesAsync( ct );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "API key '{Name}' (prefix: {Prefix}) created for user {UserId} with role {Role}.",
                apiKey.Name, apiKey.KeyPrefix, createdByUserId, role
            );
        }

        return (apiKey, rawKey);
    }

    /// <summary>
    /// Validates a raw API key and returns the corresponding entity if valid.
    /// Updates <see cref="ApiKey.LastUsedUtc"/> on successful validation.
    /// </summary>
    /// <param name="rawKey">The raw API key to validate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The API key entity if valid; otherwise <see langword="null"/>.</returns>
    public async Task<ApiKey?> ValidateAsync( string rawKey, CancellationToken ct = default ) {
        if (string.IsNullOrWhiteSpace( rawKey )) {
            return null;
        }

        string keyHash = ComputeHash( rawKey );

        ApiKey? apiKey = await dbContext.ApiKeys
            .FirstOrDefaultAsync( k => k.KeyHash == keyHash, ct );

        if (apiKey is null) {
            return null;
        }

        if (apiKey.IsRevoked) {
            logger.LogWarning( "Attempt to use revoked API key '{Name}' (prefix: {Prefix}).",
                apiKey.Name, apiKey.KeyPrefix
            );
            return null;
        }

        if (apiKey.ExpiresUtc.HasValue && apiKey.ExpiresUtc.Value < DateTime.UtcNow) {
            logger.LogWarning( "Attempt to use expired API key '{Name}' (prefix: {Prefix}).",
                apiKey.Name, apiKey.KeyPrefix
            );
            return null;
        }

        // Update last used timestamp
        apiKey.LastUsedUtc = DateTime.UtcNow;
        _ = await dbContext.SaveChangesAsync( ct );

        return apiKey;
    }

    /// <summary>
    /// Revokes an API key by ID.
    /// </summary>
    /// <param name="keyId">The API key ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> if the key was found and revoked; otherwise <see langword="false"/>.</returns>
    public async Task<bool> RevokeAsync( Guid keyId, CancellationToken ct = default ) {
        ApiKey? apiKey = await dbContext.ApiKeys.FindAsync( [keyId], ct );
        if (apiKey is null) {
            return false;
        }

        apiKey.IsRevoked = true;
        _ = await dbContext.SaveChangesAsync( ct );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "API key '{Name}' (prefix: {Prefix}) revoked.", apiKey.Name, apiKey.KeyPrefix );
        }
        return true;
    }

    /// <summary>
    /// Gets all API keys (without the raw key value).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of API key entities.</returns>
    public async Task<IReadOnlyList<ApiKey>> GetAllAsync( CancellationToken ct = default ) {
        return await dbContext.ApiKeys
            .AsNoTracking( )
            .OrderByDescending( k => k.CreatedUtc )
            .ToListAsync( ct );
    }

    /// <summary>
    /// Gets API keys created by a specific user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of API key entities.</returns>
    public async Task<IReadOnlyList<ApiKey>> GetByUserAsync( string userId, CancellationToken ct = default ) {
        return await dbContext.ApiKeys
            .AsNoTracking( )
            .Where( k => k.CreatedByUserId == userId )
            .OrderByDescending( k => k.CreatedUtc )
            .ToListAsync( ct );
    }

    /// <summary>
    /// Computes a SHA-512 hash of the raw API key.
    /// </summary>
    private static string ComputeHash( string rawKey ) {
        byte[] hash = SHA512.HashData( System.Text.Encoding.UTF8.GetBytes( rawKey ) );
        return Convert.ToHexString( hash );
    }
}
