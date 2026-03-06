using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography;
using Werkr.Common.Models;
using Werkr.Data.Entities.Interfaces;

namespace Werkr.Data.Entities.Registration;

/// <summary>
/// Tracks a pending registration bundle on the Server side.
/// Created when an admin generates a registration bundle and persisted
/// until the Agent completes the handshake or the bundle expires.
/// </summary>
[Table( "registration_bundles" )]
public class RegistrationBundle : ConcurrencyBase, IKey<Guid> {

    /// <summary>Database-generated primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public Guid Id { get; set; }

    /// <summary>Admin-assigned label for this registration, max 256 characters.</summary>
    [Required]
    [MaxLength( 256 )]
    public string ConnectionName { get; set; } = string.Empty;

    /// <summary>Server's RSA public key generated for this registration.</summary>
    public RSAParameters ServerPublicKey { get; set; }

    /// <summary>Server's RSA private key (used to decrypt the Agent's response).</summary>
    public RSAParameters ServerPrivateKey { get; set; }

    /// <summary>16-byte random correlation token embedded in the encrypted bundle.</summary>
    [Required]
    public byte[] BundleId { get; set; } = [];

    /// <summary>Current status of this registration bundle.</summary>
    public RegistrationStatus Status { get; set; } = RegistrationStatus.Pending;

    /// <summary>UTC timestamp when this bundle expires.</summary>
    [Required]
    public DateTime ExpiresAt { get; set; }

    /// <summary>RSA key size in bits used for this registration (default 4096).</summary>
    public int KeySize { get; set; } = 4096;

    /// <summary>Tags to assign to the agent upon registration completion.</summary>
    public string[] Tags { get; set; } = [];

    /// <summary>
    /// Allowed filesystem path prefixes to assign to the agent upon registration completion.
    /// Mirrors the <see cref="RegisteredConnection.AllowedPaths"/> pattern.
    /// </summary>
    public string[] AllowedPaths { get; set; } = [];
}
