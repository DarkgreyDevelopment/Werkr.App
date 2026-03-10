namespace Werkr.Data.Identity;

/// <summary>
/// Design-time factory for <see cref="PostgresWerkrIdentityDbContext"/>,
/// used by <c>dotnet ef</c> CLI when <c>--context PostgresWerkrIdentityDbContext</c> is specified.
/// </summary>
public class PostgresIdentityDesignTimeFactory : IDesignTimeDbContextFactory<PostgresWerkrIdentityDbContext> {
    /// <inheritdoc/>
    public PostgresWerkrIdentityDbContext CreateDbContext( string[] args ) {
        DbContextOptionsBuilder<PostgresWerkrIdentityDbContext> optionsBuilder = new( );
        _ = optionsBuilder.UseNpgsql(
                "Host=localhost;Database=werkr_identity_design;Username=postgres;Password=postgres",
                npgsql => npgsql.MigrationsHistoryTable( "__EFMigrationsHistory", "werkr_identity" ) )
            .UseSnakeCaseNamingConvention( );
        return new PostgresWerkrIdentityDbContext( optionsBuilder.Options );
    }
}
