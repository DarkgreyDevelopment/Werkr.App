namespace Werkr.Common.Models;

/// <summary>Agent summary for the agents list endpoint.</summary>
/// <param name="Id">Connection unique identifier.</param>
/// <param name="ConnectionName">Human-readable name.</param>
/// <param name="RemoteUrl">Agent's gRPC endpoint URL.</param>
/// <param name="Status">Current connection status.</param>
/// <param name="LastSeen">Last communication timestamp.</param>
/// <param name="RegisteredAt">When the connection was established.</param>
public sealed record AgentListDto(
    Guid Id,
    string ConnectionName,
    string RemoteUrl,
    string Status,
    DateTime? LastSeen,
    DateTime RegisteredAt );
