namespace Werkr.Common.Attributes;

/// <summary>
/// Specifies the category for an action handler, used for step palette grouping and API discovery.
/// </summary>
[AttributeUsage( AttributeTargets.Class, Inherited = false )]
public sealed class ActionCategoryAttribute : Attribute {
    /// <summary>The category name (e.g., "File", "Network", "ControlFlow").</summary>
    public string Category { get; }

    /// <summary>Initializes a new instance of the <see cref="ActionCategoryAttribute"/> class.</summary>
    /// <param name="category">The category name.</param>
    public ActionCategoryAttribute( string category ) {
        Category = category;
    }
}
