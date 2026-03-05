namespace Werkr.Core.Security;

/// <summary>
/// Cross-platform abstraction for securely storing secrets in the OS credential store.
/// Used to store the Agent's SQLCipher database passphrase.
/// </summary>
public interface ISecretStore {
    /// <summary>Retrieves a secret value by key. Returns null if the key does not exist.</summary>
    /// <param name="key">The key identifying the secret.</param>
    /// <returns>The secret value, or null if not found.</returns>
    Task<string?> GetSecretAsync( string key );

    /// <summary>Stores a secret value by key, overwriting any existing value.</summary>
    /// <param name="key">The key identifying the secret.</param>
    /// <param name="value">The secret value to store.</param>
    Task SetSecretAsync(
        string key,
        string value
    );

    /// <summary>Deletes a secret by key. No-op if the key does not exist.</summary>
    /// <param name="key">The key identifying the secret.</param>
    Task DeleteSecretAsync( string key );
}
