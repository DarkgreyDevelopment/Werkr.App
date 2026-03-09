using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Entities.Interfaces;
using Werkr.Data.Entities.Tasks;

namespace Werkr.Data.Entities.Workflows;

/// <summary>
/// Represents a workflow containing ordered tasks.
/// </summary>
[Table( "workflows" )]
public class Workflow : ConcurrencyBase, IKey<long> {

    /// <summary>Database-generated primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public long Id { get; set; }

    /// <summary>Display name of the workflow.</summary>
    [Required]
    [MaxLength( 256 )]
    public string Name { get; set; } = string.Empty;

    /// <summary>Description of the workflow.</summary>
    [MaxLength( 2000 )]
    public string Description { get; set; } = string.Empty;

    /// <summary>Whether the workflow is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Navigation property for workflow steps.</summary>
    public ICollection<WorkflowStep> Steps { get; set; } = [];

    /// <summary>Navigation property for tasks in this workflow.</summary>
    public ICollection<WerkrTask> Tasks { get; set; } = [];

    /// <summary>Navigation property for workflow runs.</summary>
    public ICollection<WorkflowRun> Runs { get; set; } = [];

    /// <summary>Navigation property for schedule links (many-to-many via WorkflowSchedule).</summary>
    public ICollection<WorkflowSchedule> WorkflowSchedules { get; set; } = [];
}
