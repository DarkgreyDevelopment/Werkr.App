using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Entities.Interfaces;

namespace Werkr.Data.Entities.Tasks;

/// <summary>
/// Immutable snapshot of a task's definition at a point in time.
/// Every task save creates a new version; workflow steps bind to specific versions.
/// </summary>
[Table( "task_versions" )]
public class TaskVersion : ConcurrencyBase, IKey<long> {

    /// <summary>Database-generated primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public long Id { get; set; }

    /// <summary>Foreign key to the parent task.</summary>
    public long TaskId { get; set; }

    /// <summary>Monotonically increasing version number within the task (1-based).</summary>
    public int VersionNumber { get; set; }

    /// <summary>
    /// JSON-serialized <see cref="TaskDefinitionSnapshot"/> capturing the full task definition at this version.
    /// </summary>
    [Required]
    public string Definition { get; set; } = string.Empty;

    /// <summary>The user who created this version. Null for system-generated versions (e.g. seeder).</summary>
    [MaxLength( 450 )]
    public string? CreatedByUserId { get; set; }

    /// <summary>Optional human-readable description of what changed in this version.</summary>
    [MaxLength( 500 )]
    public string? ChangeDescription { get; set; }

    /// <summary>Navigation property to the parent task.</summary>
    [ForeignKey( nameof( TaskId ) )]
    public WerkrTask? Task { get; set; }
}
