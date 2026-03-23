using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Entities.Interfaces;
using Werkr.Data.Entities.Tasks;

namespace Werkr.Data.Entities.Workflows;

/// <summary>
/// Represents a single execution run of a workflow.
/// </summary>
[Table( "workflow_runs" )]
public class WorkflowRun : ConcurrencyBase, IKey<Guid> {

    /// <summary>Database-generated primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public Guid Id { get; set; }

    /// <summary>Foreign key to the parent workflow.</summary>
    public long WorkflowId { get; set; }

    /// <summary>When the workflow run started (UTC).</summary>
    public DateTime StartTime { get; set; }

    /// <summary>When the workflow run ended (UTC). Null if still running.</summary>
    public DateTime? EndTime { get; set; }

    /// <summary>Current status of the workflow run.</summary>
    public WorkflowRunStatus Status { get; set; } = WorkflowRunStatus.Running;

    /// <summary>Foreign key to the workflow version that was active when this run started.</summary>
    public long? WorkflowVersionId { get; set; }

    /// <summary>Snapshot of the workflow name at run start.</summary>
    [MaxLength( 200 )]
    public string? WorkflowNameSnapshot { get; set; }

    /// <summary>Snapshot of the workflow version number at run start.</summary>
    public int? WorkflowVersionSnapshot { get; set; }

    /// <summary>Navigation to the workflow version bound to this run.</summary>
    [ForeignKey( nameof( WorkflowVersionId ) )]
    public WorkflowVersion? WorkflowVersion { get; set; }

    /// <summary>Navigation to the parent workflow.</summary>
    [ForeignKey( nameof( WorkflowId ) )]
    public Workflow? Workflow { get; set; }

    /// <summary>Navigation to jobs created during this workflow run.</summary>
    public ICollection<WerkrJob> Jobs { get; set; } = [];

    /// <summary>Navigation to runtime variable values for this run.</summary>
    public ICollection<WorkflowRunVariable> RunVariables { get; set; } = [];

    /// <summary>Navigation to step execution records for this run.</summary>
    public ICollection<WorkflowStepExecution> StepExecutions { get; set; } = [];
}
