using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Werkr.Data;

/// <summary>
/// Design-time factory for <see cref="SqliteWerkrDbContext"/>, used by <c>dotnet ef</c> CLI
/// when <c>--context SqliteWerkrDbContext</c> is specified.
/// </summary>
public class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteWerkrDbContext> {
    /// <inheritdoc/>
    public SqliteWerkrDbContext CreateDbContext( string[] args ) {
        DbContextOptionsBuilder<SqliteWerkrDbContext> optionsBuilder = new( );
        _ = optionsBuilder.UseSqlite( "Data Source=werkr_design.db" )
            .UseSnakeCaseNamingConvention( );
        return new SqliteWerkrDbContext( optionsBuilder.Options );
    }
}
