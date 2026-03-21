using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Entities.Interfaces;

namespace Werkr.Data.Entities.Configuration;

/// <summary>
/// Append-only change history for a <see cref="ConfigurationEntry"/>.
/// </summary>
[Table( "configuration_change_logs" )]
public class ConfigurationChangeLog : IKey<long> {

    /// <summary>Database-generated primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public long Id { get; set; }

    /// <summary>Foreign key to the configuration entry.</summary>
    public long ConfigurationEntryId { get; set; }

    /// <summary>Denormalized key for query convenience.</summary>
    [Required]
    [MaxLength( 256 )]
    public string Key { get; set; } = string.Empty;

    /// <summary>Previous value (null for first entry).</summary>
    public string? PreviousValue { get; set; }

    /// <summary>New value after the change.</summary>
    [Required]
    public string NewValue { get; set; } = string.Empty;

    /// <summary>User ID who made the change.</summary>
    [Required]
    [MaxLength( 128 )]
    public string ChangedByUserId { get; set; } = string.Empty;

    /// <summary>UTC timestamp of the change.</summary>
    [Required]
    public DateTime ChangedUtc { get; set; }

    /// <summary>Navigation to the parent configuration entry.</summary>
    [ForeignKey( nameof( ConfigurationEntryId ) )]
    public ConfigurationEntry? ConfigurationEntry { get; set; }
}
