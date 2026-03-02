namespace Werkr.Common.Models;

/// <summary>Response body from a successful token exchange.</summary>
public sealed record TokenResponse(
    string Token,
    DateTime ExpiresUtc );
