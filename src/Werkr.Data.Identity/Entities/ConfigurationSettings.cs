using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Werkr.Data.Entities;
using Werkr.Data.Entities.Interfaces;

namespace Werkr.Data.Identity.Entities;

/// <summary>
/// Global application configuration stored in the identity database.
/// Exactly one row exists; seeded on first startup.
/// </summary>
[Table( "config_settings" )]
public class ConfigurationSettings : ConcurrencyBase, IKey<Guid> {
    /// <summary>Unique identifier.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public Guid Id { get; set; }

    /// <summary>Default RSA key size in bits.</summary>
    public int DefaultKeySize { get; set; } = 4096;

    // ── Server identity ──────────────────────────────────────────────
    /// <summary>Display name shown in the Blazor UI header.</summary>
    [MaxLength( 200 )]
    public string ServerName { get; set; } = "Werkr Server";

    /// <summary>Whether new agent registrations are accepted.</summary>
    public bool AllowRegistration { get; set; } = true;

    // ── UI polling ───────────────────────────────────────────────────
    /// <summary>Seconds between dashboard / list auto-refresh polls.</summary>
    public int PollingIntervalSeconds { get; set; } = 30;

    /// <summary>Seconds between run-detail / workflow-run auto-refresh polls.</summary>
    public int RunDetailPollingIntervalSeconds { get; set; } = 15;
}
