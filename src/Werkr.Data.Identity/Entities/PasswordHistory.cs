using System.ComponentModel.DataAnnotations;

namespace Werkr.Data.Identity.Entities;

/// <summary>
/// Stores a historical password hash for a user to prevent password reuse.
/// </summary>
public class PasswordHistory {
    /// <summary>Primary key, database-generated.</summary>
    public long Id { get; set; }

    /// <summary>Foreign key to the owning <see cref="WerkrUser"/>.</summary>
    public string UserId { get; set; } = "";

    /// <summary>
    /// ASP.NET Identity salted password hash. Compared via
    /// <c>PasswordHasher.VerifyHashedPassword</c>, not string equality.
    /// </summary>
    [MaxLength( 1024 )]
    public string PasswordHash { get; set; } = "";

    /// <summary>UTC timestamp when this hash was recorded.</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>Navigation property to the owning user. Cascade-deleted with the user.</summary>
    public WerkrUser User { get; set; } = null!;
}
