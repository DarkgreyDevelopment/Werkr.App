namespace Werkr.Common.Models;

/// <summary>
/// Read-only projection of a <c>RetentionPolicy</c> entity for API responses.
/// </summary>
/// <param name="EntityType">The entity type this policy governs.</param>
/// <param name="RetentionDays">Number of days records are retained.</param>
/// <param name="IsEnabled">Whether the policy is actively enforced.</param>
/// <param name="ModifiedUtc">UTC timestamp of last modification.</param>
/// <param name="ModifiedByUserId">User ID of the last modifier.</param>
public sealed record RetentionPolicyDto(
    string EntityType,
    int RetentionDays,
    bool IsEnabled,
    DateTime ModifiedUtc,
    string ModifiedByUserId
);
