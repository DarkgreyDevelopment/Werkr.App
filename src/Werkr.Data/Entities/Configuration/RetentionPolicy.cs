using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Entities.Interfaces;

namespace Werkr.Data.Entities.Configuration;

/// <summary>
/// Defines how long records of a given entity type are retained before
/// the retention sweep deletes them.
/// </summary>
[Table( "retention_policies" )]
public class RetentionPolicy : ConcurrencyBase, IKey<long> {

    /// <summary>Database-generated primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public long Id { get; set; }

    /// <summary>
    /// Logical entity type this policy governs, e.g. "workflow_run" or "audit_log".
    /// Must be unique across all policies.
    /// </summary>
    [Required]
    [MaxLength( 64 )]
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Number of days to retain records before they become eligible for deletion.</summary>
    public int RetentionDays { get; set; }

    /// <summary>Whether this retention policy is actively enforced during sweeps.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>UTC timestamp of last modification.</summary>
    [Required]
    public DateTime ModifiedUtc { get; set; }

    /// <summary>User ID of the last modifier.</summary>
    [Required]
    [MaxLength( 128 )]
    public string ModifiedByUserId { get; set; } = string.Empty;
}
