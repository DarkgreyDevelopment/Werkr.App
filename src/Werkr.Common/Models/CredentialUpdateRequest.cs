namespace Werkr.Common.Models;

/// <summary>Request body for updating a credential.</summary>
public sealed record CredentialUpdateRequest(
    string? Value = null,
    string? Description = null,
    string? Type = null
);
