namespace Werkr.Data.Entities.Workflows;

/// <summary>
/// Defines how a workflow step evaluates its predecessor dependencies.
/// </summary>
public enum DependencyMode {

    /// <summary>All predecessor steps must succeed and satisfy conditions before this step executes.</summary>
    AllSuccess = 0,

    /// <summary>Any single predecessor succeeding and satisfying conditions triggers this step.</summary>
    AnySuccess = 1,
}
