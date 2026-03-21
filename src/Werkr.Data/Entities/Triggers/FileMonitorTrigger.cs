using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Data.Entities.Triggers;

/// <summary>Persistent file system trigger that watches a directory and initiates workflow runs on file events.</summary>
[Table( "file_monitor_triggers" )]
public class FileMonitorTrigger {
    /// <summary>Database-generated primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public long Id { get; set; }

    /// <summary>The workflow to trigger when a file event occurs.</summary>
    public long WorkflowId { get; set; }

    /// <summary>Directory path to watch for file events.</summary>
    [MaxLength( 500 )]
    public string WatchDirectory { get; set; } = string.Empty;

    /// <summary>Glob pattern for matching files (e.g. "*.csv", "report_*.txt").</summary>
    [MaxLength( 200 )]
    public string FilePattern { get; set; } = "*.*";

    /// <summary>JSON array of event types to listen for (e.g. ["created","changed","deleted","renamed"]).</summary>
    public string EventTypes { get; set; } = "[\"created\"]";

    /// <summary>Debounce interval in milliseconds to prevent duplicate triggers for rapid file changes.</summary>
    public int DebounceMs { get; set; } = 500;

    /// <summary>Whether this trigger is active.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Optional JSON array of agent tags for routing. Null means all agents.</summary>
    public string? TargetTags { get; set; }

    /// <summary>Foreign key to the current (latest) trigger version. Nullable for migration.</summary>
    public long? CurrentVersionId { get; set; }

    /// <summary>How this trigger resolves which workflow version to execute.</summary>
    public VersionBindingMode VersionBindingMode { get; set; } = VersionBindingMode.Latest;

    /// <summary>Foreign key to the pinned workflow version. Only used when <see cref="VersionBindingMode"/> is Pinned.</summary>
    public long? PinnedWorkflowVersionId { get; set; }

    /// <summary>Navigation property to the target workflow.</summary>
    [ForeignKey( nameof( WorkflowId ) )]
    public Workflow Workflow { get; set; } = null!;

    /// <summary>Navigation to the current (latest) version snapshot.</summary>
    [ForeignKey( nameof( CurrentVersionId ) )]
    public TriggerVersion? CurrentVersion { get; set; }

    /// <summary>All version snapshots for this trigger.</summary>
    public ICollection<TriggerVersion> Versions { get; set; } = [];

    /// <summary>Navigation to the pinned workflow version.</summary>
    [ForeignKey( nameof( PinnedWorkflowVersionId ) )]
    public WorkflowVersion? PinnedWorkflowVersion { get; set; }
}
