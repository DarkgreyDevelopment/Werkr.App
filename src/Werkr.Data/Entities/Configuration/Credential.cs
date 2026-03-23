using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Entities.Interfaces;

namespace Werkr.Data.Entities.Configuration;

/// <summary>
/// An encrypted credential stored in the database.
/// The <see cref="EncryptedValue"/> column uses field-level encryption
/// via <c>EncryptedStringConverter</c> in the DbContext.
/// </summary>
[Table( "credentials" )]
public class Credential : ConcurrencyBase, IKey<long> {

    /// <summary>Database-generated primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public long Id { get; set; }

    /// <summary>Unique display name for the credential.</summary>
    [Required]
    [MaxLength( 128 )]
    public string Name { get; set; } = string.Empty;

    /// <summary>Classification of the credential.</summary>
    public CredentialType Type { get; set; }

    /// <summary>
    /// Encrypted credential value. Transparent encryption/decryption
    /// is handled by EF Core's <c>EncryptedStringConverter</c>.
    /// </summary>
    [Required]
    public string EncryptedValue { get; set; } = string.Empty;

    /// <summary>Optional human-readable description.</summary>
    [MaxLength( 500 )]
    public string? Description { get; set; }

    /// <summary>UTC timestamp of creation.</summary>
    [Required]
    public DateTime CreatedUtc { get; set; }

    /// <summary>UTC timestamp of last modification.</summary>
    [Required]
    public DateTime ModifiedUtc { get; set; }

    /// <summary>User ID who created the credential.</summary>
    [Required]
    [MaxLength( 128 )]
    public string CreatedByUserId { get; set; } = string.Empty;

    /// <summary>User ID who last modified the credential.</summary>
    [Required]
    [MaxLength( 128 )]
    public string ModifiedByUserId { get; set; } = string.Empty;

    /// <summary>Agent scoping rules. Empty = available to all agents.</summary>
    public ICollection<CredentialAgentScope> AgentScopes { get; set; } = [];
}
