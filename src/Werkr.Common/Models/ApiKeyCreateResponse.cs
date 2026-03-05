namespace Werkr.Common.Models;

/// <summary>
/// Response body from API key creation.
/// Contains the raw key value (only available at creation time).
/// </summary>
public sealed record ApiKeyCreateResponse(
    Guid Id,
    string Name,
    string RawKey,
    string KeyPrefix,
    string Role,
    DateTime CreatedUtc,
    DateTime? ExpiresUtc
);
