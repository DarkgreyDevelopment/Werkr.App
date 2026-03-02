using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Werkr.Common;

using Werkr.Data.Identity.Entities;

namespace Werkr.Data.Identity.Extensions;

/// <summary>
/// Extension methods for registering Werkr Identity services.
/// </summary>
public static class IdentityExtensions {
    /// <summary>
    /// Registers <see cref="WerkrIdentityDbContext"/> and ASP.NET Core Identity with Werkr defaults.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="provider">The database provider to use.</param>
    /// <param name="connectionString">The database connection string.</param>
    /// <returns>The <see cref="IdentityBuilder"/> for further configuration.</returns>
    public static IdentityBuilder AddWerkrIdentity(
        this IServiceCollection services,
        DatabaseProvider provider,
        string connectionString ) {
        // Register provider-specific identity DbContext with forwarding to base type.
        // EF Core requires a distinct type per provider so each set of migrations gets
        // its own ModelSnapshot — same pattern as WerkrDbContextExtensions.
        switch (provider) {
            case DatabaseProvider.Postgres:
                _ = services.AddDbContext<PostgresWerkrIdentityDbContext>( options => {
                    _ = options.UseNpgsql( connectionString, npgsql =>
                            npgsql.MigrationsHistoryTable( "__EFMigrationsHistory", "werkr_identity" ) )
                        .UseSnakeCaseNamingConvention( );
                } );
                _ = services.AddScoped<WerkrIdentityDbContext>( sp =>
                    sp.GetRequiredService<PostgresWerkrIdentityDbContext>( ) );
                break;

            case DatabaseProvider.SQLite:
                _ = services.AddDbContext<SqliteWerkrIdentityDbContext>( options => {
                    _ = options.UseSqlite( connectionString )
                        .UseSnakeCaseNamingConvention( );
                } );
                _ = services.AddScoped<WerkrIdentityDbContext>( sp =>
                    sp.GetRequiredService<SqliteWerkrIdentityDbContext>( ) );
                break;

            default:
                throw new ArgumentOutOfRangeException( nameof( provider ), provider, "Unsupported database provider." );
        }

        // Configure Identity with NIST-aligned defaults
        return services.AddIdentity<WerkrUser, IdentityRole>( options => ConfigureIdentityOptions( options ) )
            .AddEntityFrameworkStores<WerkrIdentityDbContext>( )
            .AddDefaultTokenProviders( );
    }

    /// <summary>
    /// Configures Identity options with NIST-aligned password policy.
    /// </summary>
    public static void ConfigureIdentityOptions( IdentityOptions options ) {
        // Password policy (NIST-aligned): favor length over composition complexity.
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 1;

        // Lockout
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes( 15 );
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.AllowedForNewUsers = true;

        // User
        options.User.RequireUniqueEmail = true;

        // Sign-in
        options.SignIn.RequireConfirmedAccount = false;
    }
}
