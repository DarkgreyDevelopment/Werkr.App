namespace Werkr.Common.Models;

/// <summary>DTO for listing API keys (without the raw key value).</summary>
public sealed record ApiKeyDto(
    Guid Id,
    string Name,
    string KeyPrefix,
    string Role,
    string CreatedByUserId,
    DateTime CreatedUtc,
    DateTime? ExpiresUtc,
    bool IsRevoked,
    DateTime? LastUsedUtc );
