namespace Werkr.Common.Models;

/// <summary>Agent connection status.</summary>
public enum AgentStatus {
    /// <summary>Agent has completed registration.</summary>
    Registered = 0,

    /// <summary>Agent is currently connected and responding.</summary>
    Connected = 1,

    /// <summary>Agent is not responding.</summary>
    Disconnected = 2,

    /// <summary>Agent is in an error state.</summary>
    Error = 3,
}
