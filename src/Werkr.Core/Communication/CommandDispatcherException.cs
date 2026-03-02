namespace Werkr.Core.Communication;

/// <summary>
/// Typed exception thrown by <see cref="CommandDispatcher"/> to provide
/// actionable error context to callers without leaking raw internal details.
/// </summary>
public sealed class CommandDispatcherException : Exception {
    /// <summary>The categorized failure reason.</summary>
    public CommandDispatchFailure Reason { get; }

    /// <summary>The agent connection ID the dispatch targeted, if known.</summary>
    public Guid? AgentConnectionId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandDispatcherException"/> class.
    /// </summary>
    /// <param name="reason">The categorized failure reason.</param>
    /// <param name="message">The internal error message (for logging).</param>
    /// <param name="agentConnectionId">The agent connection ID, if known.</param>
    /// <param name="innerException">The original exception, if any.</param>
    public CommandDispatcherException(
        CommandDispatchFailure reason,
        string message,
        Guid? agentConnectionId = null,
        Exception? innerException = null )
        : base( message, innerException ) {
        Reason = reason;
        AgentConnectionId = agentConnectionId;
    }

    /// <summary>
    /// Returns a user-safe description of the failure that avoids exposing internal details.
    /// </summary>
    public string UserMessage => Reason switch {
        CommandDispatchFailure.AgentNotFound =>
            "The specified agent was not found. It may have been removed.",
        CommandDispatchFailure.AgentRevoked =>
            "The agent connection has been revoked and can no longer accept commands.",
        CommandDispatchFailure.AgentUnreachable =>
            "The agent is unreachable. Verify that it is running and network connectivity is intact.",
        CommandDispatchFailure.TlsError =>
            "A TLS/certificate error occurred communicating with the agent. Check certificate configuration.",
        CommandDispatchFailure.EncryptionError =>
            "Failed to encrypt or decrypt the command payload. The shared key may be invalid.",
        _ =>
            "An unexpected error occurred while dispatching the command."
    };
}
