namespace Werkr.Common.Models.Actions;

/// <summary>Parameters for the ForEach action.</summary>
public sealed record ForEachParameters {
    /// <summary>
    /// The property name within the input JSON object that contains the target array
    /// to iterate over (e.g. <c>"items"</c>).
    /// </summary>
    public required string ArrayPropertyName { get; init; }
}
