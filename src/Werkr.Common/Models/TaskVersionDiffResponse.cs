namespace Werkr.Common.Models;

/// <summary>Response DTO for a diff between two task versions.</summary>
public sealed record TaskVersionDiffResponse(
    long FromVersionId,
    int FromVersionNumber,
    long ToVersionId,
    int ToVersionNumber,
    IReadOnlyList<TaskVersionDiffEntry> Changes
);
