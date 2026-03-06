namespace Werkr.Data.Entities.Workflows;

/// <summary>
/// Control flow statement type for workflow steps.
/// </summary>
public enum ControlStatement {

    /// <summary>Execute sequentially after dependencies complete.</summary>
    Sequential = 0,

    /// <summary>Execute only if condition is true.</summary>
    If = 1,

    /// <summary>Execute only if prior If/ElseIf was false.</summary>
    Else = 2,

    /// <summary>Execute if prior If/ElseIf was false AND this condition is true.</summary>
    ElseIf = 3,

    /// <summary>Repeat while condition is true (evaluated before each iteration).</summary>
    While = 4,

    /// <summary>Execute once, then repeat while condition is true.</summary>
    Do = 5,
}
