namespace Werkr.Core.Credentials;

/// <summary>
/// Ambient context for server-resolved credentials available during job execution.
/// Set before running an action handler, cleared after. Handlers check this before
/// falling back to <see cref="Security.ISecretStore"/>.
/// </summary>
public static class ResolvedCredentialContext {

    private static readonly AsyncLocal<IReadOnlyDictionary<string, string>?> s_current = new( );

    /// <summary>Gets or sets the resolved credentials for the current async scope.</summary>
    public static IReadOnlyDictionary<string, string>? Current {
        get => s_current.Value;
        set => s_current.Value = value;
    }

    /// <summary>
    /// Tries to resolve a credential from the current context.
    /// Returns null if no context is set or the credential is not found.
    /// </summary>
    public static string? TryResolve( string credentialName ) =>
        Current is not null && Current.TryGetValue( credentialName, out string? value )
            ? value
            : null;
}
