namespace Werkr.Common.Models.Actions;

/// <summary>
/// Parameters for the <c>TransformJson</c> action — applies an ordered sequence of
/// JSON manipulation operations to an input document. Supports dual-mode input:
/// reads from <see cref="InputPath"/> (file) or <c>inputVariableValue</c> (workflow variable).
/// </summary>
public sealed record TransformJsonParameters {

    /// <summary>
    /// Optional file path to read the input JSON from.
    /// When set, takes precedence over <c>inputVariableValue</c>.
    /// Validated against the file-path allowlist.
    /// </summary>
    public string? InputPath { get; init; }

    /// <summary>
    /// Optional file path to write the transformed JSON result to.
    /// Validated against the file-path allowlist.
    /// <c>ActionOperatorResult.OutputVariableValue</c> is always
    /// populated regardless of this setting.
    /// </summary>
    public string? OutputPath { get; init; }

    /// <summary>
    /// Ordered array of transformation operations to apply.
    /// Each operation receives the output of the previous operation as its input.
    /// </summary>
    public required JsonTransformOperation[] Operations { get; init; }
}
