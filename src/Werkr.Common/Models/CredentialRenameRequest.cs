namespace Werkr.Common.Models;

/// <summary>Request body for renaming a credential.</summary>
public sealed record CredentialRenameRequest(
    string NewName
);
