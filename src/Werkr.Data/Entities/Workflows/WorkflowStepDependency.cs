using System.ComponentModel.DataAnnotations.Schema;

namespace Werkr.Data.Entities.Workflows;

/// <summary>
/// Join entity representing a dependency relationship between two workflow steps.
/// The step identified by <see cref="StepId"/> depends on the step identified by <see cref="DependsOnStepId"/>.
/// </summary>
[Table( "workflow_step_dependencies" )]
public class WorkflowStepDependency {

    /// <summary>The step that has the dependency (the dependent).</summary>
    public long StepId { get; set; }

    /// <summary>The step that must complete first (the predecessor).</summary>
    public long DependsOnStepId { get; set; }

    /// <summary>Navigation to the dependent step.</summary>
    [ForeignKey( nameof( StepId ) )]
    public WorkflowStep? Step { get; set; }

    /// <summary>Navigation to the predecessor step.</summary>
    [ForeignKey( nameof( DependsOnStepId ) )]
    public WorkflowStep? DependsOnStep { get; set; }
}
