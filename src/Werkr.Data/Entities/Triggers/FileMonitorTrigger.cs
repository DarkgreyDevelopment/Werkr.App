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

    /// <summary>Navigation property to the target workflow.</summary>
    [ForeignKey( nameof( WorkflowId ) )]
    public Workflow Workflow { get; set; } = null!;
}
