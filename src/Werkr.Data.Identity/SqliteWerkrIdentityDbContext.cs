using Microsoft.EntityFrameworkCore;

namespace Werkr.Data.Identity;

/// <summary>
/// SQLite-specific <see cref="WerkrIdentityDbContext"/> used for migration generation and runtime resolution.
/// EF Core requires a distinct type per provider so each set of migrations gets its own
/// <see cref="Microsoft.EntityFrameworkCore.Infrastructure.ModelSnapshot"/>.
/// </summary>
/// <remarks>Creates a new SQLite-targeted instance.</remarks>
/// <param name="options">The SQLite-configured options.</param>
public class SqliteWerkrIdentityDbContext( DbContextOptions<SqliteWerkrIdentityDbContext> options )
    : WerkrIdentityDbContext( options ) { }
