namespace Werkr.Common.Models;

/// <summary>Response DTO for a task version snapshot.</summary>
public sealed record TaskVersionDto(
    long Id,
    long TaskId,
    int VersionNumber,
    string Definition,
    DateTime CreatedUtc,
    string? CreatedByUserId,
    string? ChangeDescription
);
