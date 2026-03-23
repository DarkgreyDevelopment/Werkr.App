namespace Werkr.Common.Models;

/// <summary>
/// Request body for updating a retention policy.
/// </summary>
/// <param name="RetentionDays">New retention period in days.</param>
/// <param name="IsEnabled">Optionally enable or disable the policy. Null leaves the current value unchanged.</param>
public sealed record RetentionPolicyUpdateRequest(
    int RetentionDays,
    bool? IsEnabled = null
);
