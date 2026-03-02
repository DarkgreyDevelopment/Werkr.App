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
    /// <summary>Unique identifier.</summary>
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

    /// <summary>Navigation to the parent workflow.</summary>
    [ForeignKey( nameof( WorkflowId ) )]
    public Workflow? Workflow { get; set; }

    /// <summary>Navigation to jobs created during this workflow run.</summary>
    public ICollection<WerkrJob> Jobs { get; set; } = [];
}
