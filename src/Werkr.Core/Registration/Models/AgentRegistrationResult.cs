namespace Werkr.Core.Registration.Models;

/// <summary>
/// Result returned to the Agent after a registration attempt completes.
/// </summary>
/// <param name="Success">Whether the registration succeeded.</param>
/// <param name="ApiKey">The raw API key (Agent stores this). Null on failure.</param>
/// <param name="SharedKey">The 32-byte pre-shared AES-256 symmetric key. Null on failure.</param>
/// <param name="ErrorMessage">Error description if registration failed; success message otherwise.</param>
public sealed record AgentRegistrationResult(
    bool Success,
    string? ApiKey,
    byte[]? SharedKey,
    string? ErrorMessage
);
