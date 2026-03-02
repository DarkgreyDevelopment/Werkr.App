using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

using Werkr.Data.Identity.Entities;

namespace Werkr.Data.Identity;

/// <summary>
/// DbContext for ASP.NET Core Identity with Werkr customizations.
/// Uses a separate schema (<c>werkr_identity</c>) for Postgres.
/// </summary>
public class WerkrIdentityDbContext : IdentityDbContext<WerkrUser> {
    /// <summary>Creates a new instance configured with the specified options.</summary>
    /// <param name="options">The strongly-typed options for this context.</param>
    public WerkrIdentityDbContext( DbContextOptions<WerkrIdentityDbContext> options ) : base( options ) { }

    /// <summary>Creates a new instance for use by derived provider-specific contexts.</summary>
    /// <param name="options">The options forwarded from a derived context.</param>
    protected WerkrIdentityDbContext( DbContextOptions options ) : base( options ) { }

    /// <summary>Role-permission mapping table.</summary>
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>( );

    /// <summary>API keys for token-based authentication.</summary>
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>( );

    /// <summary>Global server configuration (single row).</summary>
    public DbSet<ConfigurationSettings> ConfigurationSettings => Set<ConfigurationSettings>( );

    /// <inheritdoc/>
    protected override void OnModelCreating( ModelBuilder builder ) {
        base.OnModelCreating( builder );

        // Set schema for Postgres (SQLite doesn't support schemas)
        if (Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite") {
            _ = builder.HasDefaultSchema( "werkr_identity" );
        }

        // Remap Identity tables to snake_case names
        _ = builder.Entity<WerkrUser>( b => {
            _ = b.ToTable( "users" );
            _ = b.Property( u => u.Name ).HasMaxLength( 256 );
        } );

        _ = builder.Entity<Microsoft.AspNetCore.Identity.IdentityRole>( b => b.ToTable( "roles" ) );
        _ = builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<string>>( b => b.ToTable( "user_roles" ) );
        _ = builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<string>>( b => b.ToTable( "user_claims" ) );
        _ = builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<string>>( b => {
            _ = b.ToTable( "user_logins" );
            _ = b.Property( l => l.LoginProvider ).HasMaxLength( 128 );
            _ = b.Property( l => l.ProviderKey ).HasMaxLength( 128 );
        } );
        _ = builder.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<string>>( b => b.ToTable( "role_claims" ) );
        _ = builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<string>>( b => {
            _ = b.ToTable( "user_tokens" );
            _ = b.Property( t => t.LoginProvider ).HasMaxLength( 128 );
            _ = b.Property( t => t.Name ).HasMaxLength( 128 );
        } );

        // RolePermission join table
        _ = builder.Entity<RolePermission>( b => {
            _ = b.ToTable( "role_permissions" );
            _ = b.HasIndex( rp => new { rp.RoleId, rp.Permission } ).IsUnique( );
            _ = b.HasOne( rp => rp.Role )
                .WithMany( )
                .HasForeignKey( rp => rp.RoleId )
                .OnDelete( DeleteBehavior.Cascade );
            _ = b.Property( rp => rp.Permission )
                .HasConversion<string>( )
                .HasMaxLength( 64 );
        } );

        // ApiKey table
        _ = builder.Entity<ApiKey>( b => {
            _ = b.ToTable( "api_keys" );
            _ = b.HasKey( k => k.Id );
            _ = b.HasIndex( k => k.KeyHash ).IsUnique( );
            _ = b.HasIndex( k => k.KeyPrefix );
            _ = b.HasOne( k => k.CreatedByUser )
                .WithMany( )
                .HasForeignKey( k => k.CreatedByUserId )
                .OnDelete( DeleteBehavior.Cascade );
        } );

        // ConfigurationSettings — single-row server config
        _ = builder.Entity<ConfigurationSettings>( b => {
            _ = b.ToTable( "config_settings" );
            _ = b.HasKey( c => c.Id );
        } );
    }
}
