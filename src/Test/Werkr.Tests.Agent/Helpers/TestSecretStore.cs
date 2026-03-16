using Werkr.Core.Security;

namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// A test <see cref="ISecretStore"/> backed by a simple in-memory dictionary.
/// </summary>
internal sealed class TestSecretStore : ISecretStore {

    private readonly Dictionary<string, string> _secrets = new( StringComparer.OrdinalIgnoreCase );

    /// <summary>Sets a secret value in the in-memory store.</summary>
    public void Set( string key, string value ) => _secrets[key] = value;

    /// <inheritdoc/>
    public Task<string?> GetSecretAsync( string key ) =>
        Task.FromResult( _secrets.TryGetValue( key, out string? value ) ? value : null );

    /// <inheritdoc/>
    public Task SetSecretAsync( string key, string value ) {
        _secrets[key] = value;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteSecretAsync( string key ) {
        _ = _secrets.Remove( key );
        return Task.CompletedTask;
    }
}
