using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Entities.Interfaces;

namespace Werkr.Data.Entities.Configuration;

/// <summary>
/// A single configuration setting with hierarchical scoping (Global → Agent).
/// </summary>
[Table( "configuration_entries" )]
public class ConfigurationEntry : ConcurrencyBase, IKey<long> {

    /// <summary>Database-generated primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public long Id { get; set; }

    /// <summary>Dotted key path, e.g. "agent.concurrency.maxTasks".</summary>
    [Required]
    [MaxLength( 256 )]
    public string Key { get; set; } = string.Empty;

    /// <summary>Current value serialized as JSON.</summary>
    [Required]
    public string Value { get; set; } = string.Empty;

    /// <summary>Value type hint: "string", "number", "boolean", "json".</summary>
    [Required]
    [MaxLength( 32 )]
    public string ValueType { get; set; } = "string";

    /// <summary>Logical category: "server", "agent", "workflow", "security", "network".</summary>
    [Required]
    [MaxLength( 64 )]
    public string Category { get; set; } = string.Empty;

    /// <summary>Human-readable description of the setting.</summary>
    [MaxLength( 500 )]
    public string? Description { get; set; }

    /// <summary>Scope level: 0 = Global, 1 = Agent.</summary>
    public int ScopeLevel { get; set; }

    /// <summary>Scope identifier. Null for Global; agent ConnectionId for Agent scope.</summary>
    [MaxLength( 128 )]
    public string? ScopeId { get; set; }

    /// <summary>Monotonically incrementing version for delta sync.</summary>
    public long SyncVersion { get; set; } = 1;

    /// <summary>Optional JSON validation rules (min, max, regex, enum values).</summary>
    public string? ValidationRules { get; set; }

    /// <summary>Seed default value for reference and reset.</summary>
    [Required]
    public string DefaultValue { get; set; } = string.Empty;

    /// <summary>UTC timestamp of creation.</summary>
    [Required]
    public DateTime CreatedUtc { get; set; }

    /// <summary>UTC timestamp of last modification.</summary>
    [Required]
    public DateTime ModifiedUtc { get; set; }

    /// <summary>User ID of the last modifier.</summary>
    [Required]
    [MaxLength( 128 )]
    public string ModifiedByUserId { get; set; } = string.Empty;

    /// <summary>Change history for this entry.</summary>
    public ICollection<ConfigurationChangeLog> ChangeLogs { get; set; } = [];
}
