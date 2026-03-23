using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Entities.Interfaces;

namespace Werkr.Data.Entities.Workflows;

/// <summary>
/// Immutable snapshot of a workflow's definition at a point in time.
/// Every workflow save creates a new version; workflow runs bind to specific versions.
/// </summary>
[Table( "workflow_versions" )]
public class WorkflowVersion : ConcurrencyBase, IKey<long> {

    /// <summary>Database-generated primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public long Id { get; set; }

    /// <summary>Foreign key to the parent workflow.</summary>
    public long WorkflowId { get; set; }

    /// <summary>Monotonically increasing version number within the workflow (1-based).</summary>
    public int VersionNumber { get; set; }

    /// <summary>
    /// JSON-serialized <see cref="WorkflowDefinitionSnapshot"/> capturing the full workflow definition at this version.
    /// </summary>
    [Required]
    public string Definition { get; set; } = string.Empty;

    /// <summary>The user who created this version. Null for system-generated versions (e.g. seeder).</summary>
    [MaxLength( 450 )]
    public string? CreatedByUserId { get; set; }

    /// <summary>Optional human-readable description of what changed in this version.</summary>
    [MaxLength( 500 )]
    public string? ChangeDescription { get; set; }

    /// <summary>Navigation property to the parent workflow.</summary>
    [ForeignKey( nameof( WorkflowId ) )]
    public Workflow? Workflow { get; set; }
}
