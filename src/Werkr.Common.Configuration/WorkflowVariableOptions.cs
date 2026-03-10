namespace Werkr.Common.Configuration;

/// <summary>
/// Configuration options for workflow variables.
/// Bound to the <c>WorkflowVariables</c> configuration section.
/// </summary>
public sealed class WorkflowVariableOptions {
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "WorkflowVariables";

    /// <summary>
    /// Maximum size in bytes for a single variable value (JSON blob).
    /// Values exceeding this limit are rejected on both the agent and API sides.
    /// Default: 65 536 (64 KB).
    /// </summary>
    public int MaxValueSizeBytes { get; set; } = 65_536;
}
