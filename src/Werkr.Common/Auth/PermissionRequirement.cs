namespace Werkr.Common.Auth;

/// <summary>
/// Authorization requirement that demands a specific <see cref="Permission"/>.
/// Shared across API (claims-based handler) and Server (DB-backed handler).
/// </summary>
public sealed class PermissionRequirement( Permission permission )
    : IAuthorizationRequirement {

    /// <summary>The permission that the user must have.</summary>
    public Permission Permission { get; } = permission;
}
