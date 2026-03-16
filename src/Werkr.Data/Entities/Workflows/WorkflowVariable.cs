using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Entities.Interfaces;

namespace Werkr.Data.Entities.Workflows;

/// <summary>
/// Design-time variable definition on a workflow.
/// Variables are named at the workflow level and can have an optional default value (JSON blob).
/// </summary>
[Table( "workflow_variables" )]
public sealed class WorkflowVariable : ConcurrencyBase, IKey<long> {

    /// <summary>Database-generated primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public long Id { get; set; }

    /// <summary>Foreign key to the parent workflow.</summary>
    public long WorkflowId { get; set; }

    /// <summary>
    /// Variable name, unique per workflow (case-insensitive).
    /// Maximum 128 characters.
    /// </summary>
    [Required]
    [MaxLength( 128 )]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional human-readable description. Maximum 500 characters.</summary>
    [MaxLength( 500 )]
    public string? Description { get; set; }

    /// <summary>Optional default value for the variable (JSON blob). Seeded at workflow run start.</summary>
    public string? DefaultValue { get; set; }

    /// <summary>Navigation to the parent workflow.</summary>
    [ForeignKey( nameof( WorkflowId ) )]
    public Workflow Workflow { get; set; } = null!;
}
