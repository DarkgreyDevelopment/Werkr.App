namespace Werkr.Server.Identity;

/// <summary>
/// Ambient async-local holder for a pre-minted user JWT.
/// When set, <see cref="AuthForwardingHandler"/> uses this token instead of
/// minting a service token. This bridges the gap between circuit-scoped
/// Blazor auth state and the pooled <see cref="DelegatingHandler"/> pipeline.
/// </summary>
public static class UserTokenContext {
    private static readonly AsyncLocal<string?> s_currentToken = new( );

    /// <summary>
    /// Gets or sets the user JWT for the current async flow.
    /// <c>null</c> means no user token is available (fall back to service token).
    /// </summary>
    public static string? CurrentToken {
        get => s_currentToken.Value;
        set => s_currentToken.Value = value;
    }
}
