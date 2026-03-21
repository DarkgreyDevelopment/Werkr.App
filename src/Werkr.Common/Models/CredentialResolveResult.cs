namespace Werkr.Common.Models;

/// <summary>
/// Result of resolving a credential for an agent, distinguishing not-found from out-of-scope.
/// </summary>
/// <param name="Found">Whether a credential with the given name exists.</param>
/// <param name="InScope">Whether the requesting agent is within the credential's scope.</param>
/// <param name="DecryptedValue">The decrypted credential value, or null if not found/out of scope.</param>
public sealed record CredentialResolveResult(
    bool Found,
    bool InScope,
    string? DecryptedValue
);
