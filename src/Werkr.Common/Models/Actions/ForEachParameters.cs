namespace Werkr.Common.Models.Actions;

/// <summary>Parameters for the ForEach iteration action.</summary>
public sealed record ForEachParameters {
    /// <summary>Expression that resolves to the collection to iterate over.</summary>
    public required string CollectionExpression { get; init; }
}
