namespace Werkr.Data.Entities.Workflows;

/// <summary>
/// Defines how a workflow step evaluates its predecessor dependencies.
/// </summary>
public enum DependencyMode {
    /// <summary>All predecessor steps must complete and satisfy conditions before this step executes.</summary>
    All = 0,
    /// <summary>Any single predecessor completing and satisfying conditions triggers this step.</summary>
    Any = 1,
}
