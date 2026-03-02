using System.ComponentModel.DataAnnotations;

namespace Werkr.Data.Entities;

/// <summary>
/// Abstract base class providing concurrency tracking fields.
/// All entities that need optimistic concurrency should inherit from this.
/// </summary>
public abstract class ConcurrencyBase {
    /// <summary>Timestamp when the entity was first created (UTC).</summary>
    [Required]
    public DateTime Created { get; set; }

    /// <summary>Timestamp when the entity was last updated (UTC).</summary>
    [Required]
    public DateTime LastUpdated { get; set; }

    /// <summary>
    /// Optimistic concurrency version. Incremented on each save.
    /// </summary>
    [ConcurrencyCheck]
    public int Version { get; set; }
}
