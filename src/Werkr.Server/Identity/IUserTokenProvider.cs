namespace Werkr.Server.Identity;

/// <summary>
/// Resolves a short-lived JWT bearing the current Blazor user's identity,
/// roles, and permissions for forwarding to the API.
/// </summary>
public interface IUserTokenProvider {
    /// <summary>
    /// Gets a user-scoped JWT for the currently authenticated user,
    /// or <c>null</c> if no user is authenticated.
    /// </summary>
    Task<string?> GetTokenAsync( );
}
