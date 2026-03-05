namespace Werkr.Common.Models;

/// <summary>Request body for creating a new API key.</summary>
public sealed record ApiKeyCreateRequest(
    string Name,
    DateTime? ExpiresUtc = null
);
