using Microsoft.AspNetCore.Identity;

namespace Werkr.Data.Identity.Entities;

/// <summary>
/// Application user extending ASP.NET Core Identity.
/// </summary>
public class WerkrUser : IdentityUser {
    /// <summary>Display name for the user.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether the user account is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whether the user must change their password on next login.</summary>
    public bool ChangePassword { get; set; }

    /// <summary>Whether the user is required to enroll in 2FA.</summary>
    public bool Requires2FA { get; set; }

    /// <summary>Timestamp of the user's most recent successful login (UTC).</summary>
    public DateTime? LastLoginUtc { get; set; }
}
