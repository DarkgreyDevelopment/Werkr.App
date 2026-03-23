using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Entities.Interfaces;

namespace Werkr.Data.Entities.Settings;

/// <summary>
/// Per-user key-value preference. Uses an unconstrained <see cref="UserId"/> string
/// because Identity users live in a separate DbContext (no cross-context FK).
/// Follows the same pattern as <see cref="SavedFilter"/>.
/// </summary>
[Table( "user_preferences" )]
public class UserPreference : ConcurrencyBase, IKey<long> {

    /// <summary>Database-generated primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public long Id { get; set; }

    /// <summary>Identity user ID (unconstrained — no FK to Identity context).</summary>
    [Required]
    [MaxLength( 450 )]
    public string UserId { get; set; } = "";

    /// <summary>Preference key (e.g. <c>DisplayTimeZoneId</c>).</summary>
    [Required]
    [MaxLength( 100 )]
    public string Key { get; set; } = "";

    /// <summary>Preference value.</summary>
    [Required]
    [MaxLength( 500 )]
    public string Value { get; set; } = "";
}
