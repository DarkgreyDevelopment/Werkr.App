using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Werkr.Data;

/// <summary>
/// Design-time factory for <see cref="PostgresWerkrDbContext"/>, used by <c>dotnet ef</c> CLI
/// when <c>--context PostgresWerkrDbContext</c> is specified.
/// </summary>
public class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresWerkrDbContext> {
    /// <inheritdoc/>
    public PostgresWerkrDbContext CreateDbContext( string[] args ) {
        DbContextOptionsBuilder<PostgresWerkrDbContext> optionsBuilder = new( );
        _ = optionsBuilder.UseNpgsql( "Host=localhost;Database=werkr_design;Username=postgres;Password=postgres", npgsql =>
                npgsql.MigrationsHistoryTable( "__EFMigrationsHistory", "werkr" ) )
            .UseSnakeCaseNamingConvention( );
        return new PostgresWerkrDbContext( optionsBuilder.Options );
    }
}
