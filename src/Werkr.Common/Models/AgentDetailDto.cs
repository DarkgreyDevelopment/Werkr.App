namespace Werkr.Common.Models;

/// <summary>Detailed view for a single agent.</summary>
public sealed record AgentDetailDto(
    Guid Id,
    string ConnectionName,
    string RemoteUrl,
    string Status,
    string RsaKeyFingerprint,
    DateTime RegisteredAt,
    DateTime? LastSeen,
    bool? PowerShellAvailable,
    bool? SystemShellAvailable );
