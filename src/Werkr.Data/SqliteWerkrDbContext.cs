using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Werkr.Data;

/// <summary>
/// SQLite-specific <see cref="WerkrDbContext"/> used for migration generation and runtime resolution.
/// EF Core requires a distinct type per provider so each set of migrations gets its own
/// <see cref="ModelSnapshot"/>.
/// </summary>
/// <remarks>Creates a new SQLite-targeted instance.</remarks>
/// <param name="options">The SQLite-configured options.</param>
public class SqliteWerkrDbContext( DbContextOptions<SqliteWerkrDbContext> options )
    : WerkrDbContext( options ) { }
