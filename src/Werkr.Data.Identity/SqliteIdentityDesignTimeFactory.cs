namespace Werkr.Data.Identity;

/// <summary>
/// Design-time factory for <see cref="SqliteWerkrIdentityDbContext"/>,
/// used by <c>dotnet ef</c> CLI when <c>--context SqliteWerkrIdentityDbContext</c> is specified.
/// </summary>
public class SqliteIdentityDesignTimeFactory : IDesignTimeDbContextFactory<SqliteWerkrIdentityDbContext> {
    /// <inheritdoc/>
    public SqliteWerkrIdentityDbContext CreateDbContext( string[] args ) {
        DbContextOptionsBuilder<SqliteWerkrIdentityDbContext> optionsBuilder = new( );
        _ = optionsBuilder.UseSqlite( "Data Source=werkr_identity_design.db" )
            .UseSnakeCaseNamingConvention( );
        return new SqliteWerkrIdentityDbContext( optionsBuilder.Options );
    }
}
