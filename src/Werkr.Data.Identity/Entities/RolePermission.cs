using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Identity;

using Werkr.Common.Auth;

namespace Werkr.Data.Identity.Entities;

/// <summary>
/// Join entity mapping a role to a permission. Enables role-based permission checking
/// and future custom role creation by end administrators.
/// </summary>
public class RolePermission {
    /// <summary>Primary key.</summary>
    public long Id { get; set; }

    /// <summary>The Identity role ID (FK to <c>roles</c> table).</summary>
    [Required]
    public string RoleId { get; set; } = string.Empty;

    /// <summary>Navigation property to the Identity role.</summary>
    public IdentityRole? Role { get; set; }

    /// <summary>The permission granted to this role.</summary>
    [Required]
    public Permission Permission { get; set; }
}
