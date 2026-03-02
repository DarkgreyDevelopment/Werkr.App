using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Werkr.Common;

namespace Werkr.Data;

/// <summary>
/// Extension methods for registering <see cref="WerkrDbContext"/> with DI.
/// </summary>
public static class WerkrDbContextExtensions {
    /// <summary>
    /// Registers <see cref="WerkrDbContext"/> with the specified database provider and connection string.
    /// Uses provider-specific derived contexts so that EF Core migrations resolve correctly at runtime.
    /// </summary>
    public static IServiceCollection AddWerkrDbContext(
        this IServiceCollection services,
        DatabaseProvider provider,
        string connectionString ) {
        switch (provider) {
            case DatabaseProvider.Postgres:
                _ = services.AddDbContext<PostgresWerkrDbContext>( options => {
                    _ = options.UseNpgsql( connectionString, npgsql =>
                            npgsql.MigrationsHistoryTable( "__EFMigrationsHistory", "werkr" ) )
                        .UseSnakeCaseNamingConvention( );
                } );
                _ = services.AddScoped<WerkrDbContext>( sp => sp.GetRequiredService<PostgresWerkrDbContext>( ) );
                break;

            case DatabaseProvider.SQLite:
                _ = services.AddDbContext<SqliteWerkrDbContext>( options => {
                    _ = options.UseSqlite( connectionString )
                        .UseSnakeCaseNamingConvention( );
                } );
                _ = services.AddScoped<WerkrDbContext>( sp => sp.GetRequiredService<SqliteWerkrDbContext>( ) );
                break;

            default:
                throw new ArgumentOutOfRangeException( nameof( provider ), provider, "Unsupported database provider." );
        }

        return services;
    }
}
