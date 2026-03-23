namespace Werkr.Common.Models;

/// <summary>Status of an established connection between Server and Agent.</summary>
public enum ConnectionStatus {
    /// <summary>Connection is active and operational.</summary>
    Connected = 0,

    /// <summary>Remote endpoint is not responding.</summary>
    Disconnected = 1,

    /// <summary>Connection is in an error state.</summary>
    Error = 2,

    /// <summary>Connection has been revoked by an admin.</summary>
    Revoked = 3,
}
