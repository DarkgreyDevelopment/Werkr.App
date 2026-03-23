using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Entities.Interfaces;

namespace Werkr.Data.Entities.Triggers;

/// <summary>
/// Immutable snapshot of a trigger's definition at a point in time.
/// Every trigger save creates a new version.
/// </summary>
[Table( "trigger_versions" )]
public class TriggerVersion : ConcurrencyBase, IKey<long> {

    /// <summary>Database-generated primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public long Id { get; set; }

    /// <summary>Foreign key to the parent trigger.</summary>
    public long TriggerId { get; set; }

    /// <summary>Monotonically increasing version number within the trigger (1-based).</summary>
    public int VersionNumber { get; set; }

    /// <summary>
    /// JSON-serialized <see cref="TriggerDefinitionSnapshot"/> capturing the full trigger definition at this version.
    /// </summary>
    [Required]
    public string Definition { get; set; } = string.Empty;

    /// <summary>The user who created this version. Null for system-generated versions (e.g. seeder).</summary>
    [MaxLength( 450 )]
    public string? CreatedByUserId { get; set; }

    /// <summary>Optional human-readable description of what changed in this version.</summary>
    [MaxLength( 500 )]
    public string? ChangeDescription { get; set; }

    /// <summary>Navigation property to the parent trigger.</summary>
    [ForeignKey( nameof( TriggerId ) )]
    public FileMonitorTrigger? Trigger { get; set; }
}
