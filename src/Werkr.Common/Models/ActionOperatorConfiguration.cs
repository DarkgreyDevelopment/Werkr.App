namespace Werkr.Common.Models;

/// <summary>
/// Configuration options for the <c>ActionOperator</c> — the built-in action
/// handler dispatch engine. Bound from the <c>"ActionOperator"</c> configuration section.
/// </summary>
public sealed class ActionOperatorConfiguration {

    /// <summary>The configuration section name.</summary>
    public const string SectionName = "ActionOperator";

    /// <summary>
    /// Default timeout applied to each action handler invocation.
    /// Set to <c>null</c> to disable the timeout entirely.
    /// Default: 1 hour.
    /// </summary>
    public TimeSpan? DefaultTimeout { get; set; } = TimeSpan.FromHours( 1 );
}
