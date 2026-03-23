namespace Werkr.Common.Models;

/// <summary>Response DTO for a diff between two workflow versions.</summary>
public sealed record WorkflowVersionDiffResponse(
    long FromVersionId,
    int FromVersionNumber,
    long ToVersionId,
    int ToVersionNumber,
    IReadOnlyList<WorkflowVersionDiffEntry> Changes
);
