using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Common.Models;
using Werkr.Data.Entities.Interfaces;
using Werkr.Data.Entities.Tasks;

namespace Werkr.Data.Entities.Workflows;

/// <summary>
/// Tracks the execution status of a single workflow step within a run.
/// Supports multiple attempts for retry scenarios.
/// </summary>
[Table( "workflow_step_executions" )]
public class WorkflowStepExecution : ConcurrencyBase, IKey<long> {

    /// <summary>Database-generated primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public long Id { get; set; }

    /// <summary>Foreign key to the workflow run.</summary>
    public Guid WorkflowRunId { get; set; }

    /// <summary>Foreign key to the workflow step.</summary>
    public long StepId { get; set; }

    /// <summary>Attempt number (1-based). Incremented on retry.</summary>
    public int Attempt { get; set; } = 1;

    /// <summary>Current execution status of this step attempt.</summary>
    public StepExecutionStatus Status { get; set; } = StepExecutionStatus.Pending;

    /// <summary>When the step started executing (UTC). Null if still pending.</summary>
    public DateTime? StartTime { get; set; }

    /// <summary>When the step finished executing (UTC). Null if still running or pending.</summary>
    public DateTime? EndTime { get; set; }

    /// <summary>Foreign key to the job created for this step execution. Null until a job is dispatched.</summary>
    public Guid? JobId { get; set; }

    /// <summary>Error message if the step failed.</summary>
    [MaxLength( 4000 )]
    public string? ErrorMessage { get; set; }

    /// <summary>Reason the step was skipped (e.g., control flow evaluation).</summary>
    [MaxLength( 2000 )]
    public string? SkipReason { get; set; }

    /// <summary>Navigation property to the workflow run.</summary>
    [ForeignKey( nameof( WorkflowRunId ) )]
    public WorkflowRun? WorkflowRun { get; set; }

    /// <summary>Navigation property to the workflow step.</summary>
    [ForeignKey( nameof( StepId ) )]
    public WorkflowStep? Step { get; set; }

    /// <summary>Navigation property to the associated job.</summary>
    [ForeignKey( nameof( JobId ) )]
    public WerkrJob? Job { get; set; }
}
