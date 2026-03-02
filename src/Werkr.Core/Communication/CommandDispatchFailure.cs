namespace Werkr.Core.Communication;

/// <summary>
/// Categorizes the reason a command dispatch failed.
/// </summary>
public enum CommandDispatchFailure {
    /// <summary>The agent connection ID was not found in the database.</summary>
    AgentNotFound,

    /// <summary>The agent connection exists but has been revoked.</summary>
    AgentRevoked,

    /// <summary>The agent is unreachable (gRPC transport failure, DNS, timeout).</summary>
    AgentUnreachable,

    /// <summary>TLS handshake or certificate validation failed.</summary>
    TlsError,

    /// <summary>Payload encryption or decryption failed.</summary>
    EncryptionError,

    /// <summary>An unknown or uncategorized error occurred.</summary>
    Unknown
}
