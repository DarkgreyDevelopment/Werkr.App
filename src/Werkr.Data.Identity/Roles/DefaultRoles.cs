namespace Werkr.Data.Identity.Roles;

/// <summary>
/// Default roles for the Werkr application.
/// Simplified to 3 roles per plan spec (expandable later).
/// </summary>
public enum DefaultRoles {
    /// <summary>Full system administration access.</summary>
    Admin = 0,

    /// <summary>Can execute tasks and manage workflows.</summary>
    Operator = 1,

    /// <summary>Read-only access.</summary>
    Viewer = 2,
}
