namespace Werkr.Data.Identity;

/// <summary>
/// Postgres-specific <see cref="WerkrIdentityDbContext"/> used for migration generation and runtime resolution.
/// EF Core requires a distinct type per provider so each set of migrations gets its own
/// <see cref="Microsoft.EntityFrameworkCore.Infrastructure.ModelSnapshot"/>.
/// </summary>
/// <remarks>Creates a new Postgres-targeted instance.</remarks>
/// <param name="options">The Postgres-configured options.</param>
public class PostgresWerkrIdentityDbContext( DbContextOptions<PostgresWerkrIdentityDbContext> options )
    : WerkrIdentityDbContext( options ) { }
