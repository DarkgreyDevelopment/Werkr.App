using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Entities.Interfaces;
using Werkr.Data.Entities.Registration;
using Werkr.Data.Entities.Tasks;

namespace Werkr.Data.Entities.Workflows;

/// <summary>
/// Represents a step within a workflow, defining execution order, dependencies,
/// and optional control flow (If/Else/ElseIf/While/Do).
/// </summary>
[Table( "workflow_steps" )]
public class WorkflowStep : ConcurrencyBase, IKey<long> {

    /// <summary>Database-generated primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public long Id { get; set; }

    /// <summary>Foreign key to the parent workflow.</summary>
    public long WorkflowId { get; set; }

    /// <summary>Foreign key to the task for this step.</summary>
    public long TaskId { get; set; }

    /// <summary>Execution order within the workflow (lower = earlier). Used as tiebreaker within topological levels.</summary>
    public int Order { get; set; }

    /// <summary>Control flow statement type for this step.</summary>
    public ControlStatement ControlStatement { get; set; } = ControlStatement.Default;

    /// <summary>
    /// Condition expression evaluated against prior step results.
    /// Supports: <c>$exitCode == 0</c>, <c>$exitCode != 0</c>, <c>$exitCode &gt; N</c>,
    /// <c>$? -eq $true</c>, <c>$? -eq $false</c>, and custom expressions.
    /// Null/empty = always true (for Default steps).
    /// </summary>
    [MaxLength( 2000 )]
    public string? ConditionExpression { get; set; }

    /// <summary>
    /// Maximum iterations for While/Do loops. Safety guard to prevent infinite loops.
    /// Default: 100. Only applicable when ControlStatement is While or Do.
    /// </summary>
    public int MaxIterations { get; set; } = 100;

    /// <summary>
    /// Optional: pin this step to a specific agent, bypassing TargetTags resolution.
    /// If null, the agent is resolved at runtime via the step's task TargetTags.
    /// </summary>
    public Guid? AgentConnectionIdOverride { get; set; }

    /// <summary>
    /// How this step evaluates its predecessor dependencies.
    /// AllSuccess = all predecessors must satisfy the step's condition (default).
    /// AnySuccess = at least one predecessor satisfying the condition triggers execution.
    /// </summary>
    public DependencyMode DependencyMode { get; set; } = DependencyMode.AllSuccess;

    /// <summary>Name of the input variable consumed by this step. Null if no input variable is declared.</summary>
    [MaxLength( 128 )]
    public string? InputVariableName { get; set; }

    /// <summary>Name of the output variable produced by this step. Null if no output variable is declared.</summary>
    [MaxLength( 128 )]
    public string? OutputVariableName { get; set; }

    /// <summary>Navigation property to the parent workflow.</summary>
    [ForeignKey( nameof( WorkflowId ) )]
    public Workflow? Workflow { get; set; }

    /// <summary>Navigation to the task associated with this step. Required for eager loading in WorkflowExecutor.</summary>
    [ForeignKey( nameof( TaskId ) )]
    public WerkrTask? Task { get; set; }

    /// <summary>Navigation to the overridden agent connection.</summary>
    [ForeignKey( nameof( AgentConnectionIdOverride ) )]
    public RegisteredConnection? AgentConnectionOverride { get; set; }

    /// <summary>Navigation to dependency relationships where this step is the dependent.</summary>
    public ICollection<WorkflowStepDependency> Dependencies { get; set; } = [];

    /// <summary>Navigation to dependency relationships where this step is a predecessor.</summary>
    public ICollection<WorkflowStepDependency> Dependents { get; set; } = [];
}
