using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Entities.Tasks;

namespace Werkr.Data.Entities.Workflows;

/// <summary>
/// Append-only runtime variable value stored for a workflow run.
/// Each write creates a new row with an incremented <see cref="Version"/>.
/// The current value is the row with the highest version per (RunId, VariableName).
/// </summary>
[Table( "workflow_run_variables" )]
public sealed class WorkflowRunVariable {

    /// <summary>Database-generated primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public long Id { get; set; }

    /// <summary>Foreign key to the parent workflow run.</summary>
    public Guid WorkflowRunId { get; set; }

    /// <summary>
    /// Variable name (denormalized — survives variable renames/deletes).
    /// Maximum 128 characters.
    /// </summary>
    [Required]
    [MaxLength( 128 )]
    public string VariableName { get; set; } = string.Empty;

    /// <summary>JSON blob containing the variable value.</summary>
    [Required]
    public string Value { get; set; } = string.Empty;

    /// <summary>Monotonically increasing version number per (RunId, VariableName).</summary>
    public int Version { get; set; }

    /// <summary>Foreign key to the workflow step that produced this value. Null for manual/default entries.</summary>
    public long? ProducedByStepId { get; set; }

    /// <summary>Foreign key to the job that produced this value. Null for manual/default entries.</summary>
    public Guid? ProducedByJobId { get; set; }

    /// <summary>Origin of this variable value.</summary>
    public VariableSource Source { get; set; }

    /// <summary>Timestamp when this version was created (UTC).</summary>
    [Required]
    public DateTime Created { get; set; }

    /// <summary>Navigation to the parent workflow run.</summary>
    [ForeignKey( nameof( WorkflowRunId ) )]
    public WorkflowRun WorkflowRun { get; set; } = null!;

    /// <summary>Navigation to the step that produced this value.</summary>
    [ForeignKey( nameof( ProducedByStepId ) )]
    public WorkflowStep? ProducedByStep { get; set; }

    /// <summary>Navigation to the job that produced this value.</summary>
    [ForeignKey( nameof( ProducedByJobId ) )]
    public WerkrJob? ProducedByJob { get; set; }
}
