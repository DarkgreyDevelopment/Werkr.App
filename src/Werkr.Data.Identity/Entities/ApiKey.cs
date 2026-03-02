using System.ComponentModel.DataAnnotations;

namespace Werkr.Data.Identity.Entities;

/// <summary>
/// Represents an API key that can be exchanged for a short-lived JWT bearer token.
/// API keys inherit the role of their creator at creation time.
/// </summary>
public class ApiKey {
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The hashed API key value (SHA-256).
    /// The raw key is only returned once at creation time.
    /// </summary>
    [Required]
    [MaxLength( 128 )]
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>
    /// A short prefix of the key for identification (e.g., first 8 chars).
    /// Stored in plain text for display purposes.
    /// </summary>
    [Required]
    [MaxLength( 16 )]
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>Human-readable name for the API key.</summary>
    [Required]
    [MaxLength( 200 )]
    public string Name { get; set; } = string.Empty;

    /// <summary>The role inherited from the creator at key creation time.</summary>
    [Required]
    [MaxLength( 64 )]
    public string Role { get; set; } = string.Empty;

    /// <summary>The user ID of the creator (FK to <c>users</c> table).</summary>
    [Required]
    public string CreatedByUserId { get; set; } = string.Empty;

    /// <summary>Navigation property to the creator.</summary>
    public WerkrUser? CreatedByUser { get; set; }

    /// <summary>When the API key was created (UTC).</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional expiration date (UTC). Null means the key does not expire.
    /// </summary>
    public DateTime? ExpiresUtc { get; set; }

    /// <summary>Whether the API key has been revoked.</summary>
    public bool IsRevoked { get; set; }

    /// <summary>When the key was last used to obtain a token (UTC).</summary>
    public DateTime? LastUsedUtc { get; set; }
}
