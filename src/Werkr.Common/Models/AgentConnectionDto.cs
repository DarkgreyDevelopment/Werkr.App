namespace Werkr.Common.Models;

/// <summary>
/// Lightweight connection metadata for the Server UI.
/// Sensitive fields (OutboundApiKey, LocalPrivateKey, SharedKey) are never exposed.
/// </summary>
/// <param name="Id">Connection unique identifier.</param>
/// <param name="ConnectionName">Human-readable name.</param>
/// <param name="RemoteUrl">Agent's gRPC endpoint URL.</param>
/// <param name="Status">Current connection status.</param>
/// <param name="LastSeen">Last communication timestamp.</param>
/// <param name="RegisteredAt">When the connection was established.</param>
/// <param name="Tags">Tags associated with this agent connection.</param>
public sealed record AgentConnectionDto(
    Guid Id,
    string ConnectionName,
    string RemoteUrl,
    string Status,
    DateTime? LastSeen,
    DateTime RegisteredAt,
    string[] Tags );
