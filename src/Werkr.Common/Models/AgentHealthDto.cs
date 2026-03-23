namespace Werkr.Common.Models;

/// <summary>Health summary for a single agent.</summary>
public sealed record AgentHealthDto(
    Guid AgentId,
    string ConnectionName,
    string Status,
    bool? PowerShellAvailable,
    bool? SystemShellAvailable,
    DateTime? LastSeen,
    DateTime? HealthCheckedAt,
    string? AgentVersion = null
);
