namespace Werkr.Data.Entities.Workflows;

/// <summary>Identifies the type of composite node in a workflow DAG.</summary>
public enum CompositeType {
    /// <summary>Not a composite node.</summary>
    None = 0,
    /// <summary>Iterates over a collection variable, executing the child workflow once per element.</summary>
    ForEach = 1,
    /// <summary>Evaluates a condition before each iteration. Reserved for future use.</summary>
    While = 2,
    /// <summary>Evaluates a condition after each iteration. Reserved for future use.</summary>
    Do = 3,
    /// <summary>Routes to a matching case branch. Reserved for future use.</summary>
    Switch = 4,
}
