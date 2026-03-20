namespace Werkr.Common.Attributes;

/// <summary>
/// Specifies the category for an action handler, used for step palette grouping and API discovery.
/// </summary>
/// <remarks>Initializes a new instance of the <see cref="ActionCategoryAttribute"/> class.</remarks>
/// <param name="category">The category name.</param>
[AttributeUsage( AttributeTargets.Class, Inherited = false )]
public sealed class ActionCategoryAttribute( string category ) : Attribute {
    /// <summary>The category name (e.g., "File", "Network", "ControlFlow").</summary>
    public string Category { get; } = category;
}
