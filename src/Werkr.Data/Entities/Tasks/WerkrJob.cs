using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Werkr.Data.Entities.Interfaces;
using Werkr.Data.Entities.Registration;

namespace Werkr.Data.Entities.Tasks;

/// <summary>
/// Represents a runtime job instance created from a task.
/// </summary>
[Table( "jobs" )]
public class WerkrJob : ConcurrencyBase, IKey<Guid> {
    /// <summary>Unique identifier.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public Guid Id { get; set; }

    /// <summary>Foreign key to the source task.</summary>
    public long TaskId { get; set; }

    /// <summary>Snapshot of the task content at job creation time.</summary>
    [MaxLength( 8000 )]
    public string TaskSnapshot { get; set; } = string.Empty;

    /// <summary>How long the job ran in seconds.</summary>
    public double RuntimeSeconds { get; set; }

    /// <summary>When the job started (UTC).</summary>
    public DateTime StartTime { get; set; }

    /// <summary>When the job finished (UTC).</summary>
    public DateTime? EndTime { get; set; }

    /// <summary>Whether the job completed successfully.</summary>
    public bool Success { get; set; }

    /// <summary>Foreign key to the agent that executed this job.</summary>
    public Guid? AgentConnectionId { get; set; }

    /// <summary>The process/shell exit code, if applicable.</summary>
    public int? ExitCode { get; set; }

    /// <summary>Categorized error type when the job fails.</summary>
    public ErrorCategory ErrorCategory { get; set; } = ErrorCategory.None;

    /// <summary>
    /// Truncated tail preview of the job output (last ~2000 characters).
    /// Full output is stored on disk at <see cref="OutputPath"/>.
    /// </summary>
    [MaxLength( 2000 )]
    public string? Output { get; set; }

    /// <summary>
    /// Relative path to the full output log file on disk.
    /// Format: <c>{JobId}.log</c> under the configured job output directory.
    /// </summary>
    [MaxLength( 512 )]
    public string? OutputPath { get; set; }

    /// <summary>Foreign key to the workflow run, if this job was created as part of a workflow.</summary>
    public Guid? WorkflowRunId { get; set; }

    /// <summary>Navigation property to the source task.</summary>
    [ForeignKey( nameof( TaskId ) )]
    public WerkrTask? Task { get; set; }

    /// <summary>Navigation property to the agent that executed this job.</summary>
    [ForeignKey( nameof( AgentConnectionId ) )]
    public RegisteredConnection? AgentConnection { get; set; }

    /// <summary>Navigation property to the workflow run.</summary>
    [ForeignKey( nameof( WorkflowRunId ) )]
    public Workflows.WorkflowRun? WorkflowRun { get; set; }
}
