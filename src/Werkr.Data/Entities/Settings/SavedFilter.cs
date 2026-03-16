using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Entities.Interfaces;

namespace Werkr.Data.Entities.Settings;

/// <summary>
/// Named filter combination persisted on the server for cross-device / shared access.
/// </summary>
[Table( "saved_filters" )]
public class SavedFilter : ConcurrencyBase, IKey<long> {

    /// <summary>Database-generated primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public long Id { get; set; }

    /// <summary>Identity user ID of the filter owner.</summary>
    [Required]
    [MaxLength( 450 )]
    public string OwnerId { get; set; } = "";

    /// <summary>Page key identifying which list page this filter applies to.</summary>
    [Required]
    [MaxLength( 50 )]
    public string PageKey { get; set; } = "";

    /// <summary>User-chosen display name for the filter.</summary>
    [Required]
    [MaxLength( 200 )]
    public string Name { get; set; } = "";

    /// <summary>Serialized <c>FilterCriteria</c> JSON. Maximum 4 KB.</summary>
    [Required]
    [MaxLength( 4096 )]
    public string CriteriaJson { get; set; } = "{}";

    /// <summary>When <see langword="true"/>, the filter is visible to all users.</summary>
    public bool IsShared { get; set; }
}
