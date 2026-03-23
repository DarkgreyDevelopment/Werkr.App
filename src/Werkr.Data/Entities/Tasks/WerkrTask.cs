using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Entities.Interfaces;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Data.Entities.Tasks;

/// <summary>
/// Represents a single executable task.
/// </summary>
[Table( "tasks" )]
public class WerkrTask : ConcurrencyBase, IKey<long> {

    /// <summary>Database-generated primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public long Id { get; set; }

    /// <summary>Display name.</summary>
    [Required]
    [MaxLength( 256 )]
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable description of the task.</summary>
    [MaxLength( 2000 )]
    public string Description { get; set; } = string.Empty;

    /// <summary>The type of action this task performs.</summary>
    public TaskActionType ActionType { get; set; }

    /// <summary>Foreign key to the parent workflow, if part of one.</summary>
    public long? WorkflowId { get; set; }

    /// <summary>The command or script content to execute.</summary>
    [MaxLength( 8000 )]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Optional arguments for script-type tasks.
    /// Passed to ExecuteScriptAsync when <see cref="ActionType"/>
    /// is <see cref="TaskActionType.PowerShellScript"/> or <see cref="TaskActionType.ShellScript"/>.
    /// Stored as a JSON column.
    /// </summary>
    public string[]? Arguments { get; set; }

    /// <summary>
    /// Tags for agent targeting. An agent is selected when any of its
    /// <see cref="Registration.RegisteredConnection.Tags"/> matches any of these target tags (case-insensitive).
    /// Stored as a JSON column.
    /// </summary>
    public string[] TargetTags { get; set; } = [];

    /// <summary>Whether this task is enabled for scheduled execution.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Indicates this task was created for a single ad-hoc execution
    /// (e.g. console command) and should not appear in the normal task list.
    /// </summary>
    public bool IsEphemeral { get; set; }

    /// <summary>
    /// Maximum minutes the task may run before being cancelled.
    /// Null defaults to 60 minutes in JobExecutionService.
    /// </summary>
    [DefaultValue( 60 )]
    public long? TimeoutMinutes { get; set; }

    /// <summary>
    /// Agent schedule-sync interval in minutes. Randomized between 30-60
    /// at task creation time and fixed thereafter.
    /// </summary>
    public int SyncIntervalMinutes { get; set; }

    /// <summary>
    /// Optional expression evaluated against the operator result
    /// to determine if a job succeeded. When null, defaults are inferred from <see cref="ActionType"/>.
    /// </summary>
    [MaxLength( 500 )]
    public string? SuccessCriteria { get; set; }

    /// <summary>
    /// The specific built-in action name (e.g. "CopyFile", "StartProcess").
    /// Required when <see cref="ActionType"/> is <see cref="TaskActionType.Action"/>;
    /// must be null otherwise.
    /// </summary>
    [MaxLength( 30 )]
    public string? ActionSubType { get; set; }

    /// <summary>
    /// JSON-serialized parameters for the built-in action.
    /// Required when <see cref="ActionType"/> is <see cref="TaskActionType.Action"/>;
    /// must be null otherwise. No max length - <c>WriteContent</c> payloads may be large.
    /// </summary>
    public string? ActionParameters { get; set; }

    /// <summary>
    /// Foreign key to the current (latest) task version.
    /// Nullable for migration — existing tasks will be backfilled by <see cref="Seeding.TaskVersionSeeder"/>.
    /// </summary>
    public long? CurrentVersionId { get; set; }

    /// <summary>Navigation property for parent workflow.</summary>
    [ForeignKey( nameof( WorkflowId ) )]
    public Workflow? Workflow { get; set; }

    /// <summary>Navigation to the current (latest) version snapshot.</summary>
    [ForeignKey( nameof( CurrentVersionId ) )]
    public TaskVersion? CurrentVersion { get; set; }

    /// <summary>All version snapshots for this task.</summary>
    public ICollection<TaskVersion> Versions { get; set; } = [];

    /// <summary>Navigation property for schedule links (many-to-many via TaskSchedule).</summary>
    public ICollection<TaskSchedule> TaskSchedules { get; set; } = [];
}
