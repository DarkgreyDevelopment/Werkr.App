using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Werkr.Common;
using Werkr.Data.Encryption;

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
        string connectionString
    ) {
        switch (provider) {
            case DatabaseProvider.Postgres:
                _ = services.AddDbContext<PostgresWerkrDbContext>( options => {
                    _ = options.UseNpgsql( connectionString, npgsql =>
                            npgsql.MigrationsHistoryTable( "__EFMigrationsHistory", "werkr" ) )
                        .UseSnakeCaseNamingConvention( );
                } );
                _ = services.AddScoped<WerkrDbContext>( sp => {
                    PostgresWerkrDbContext ctx = sp.GetRequiredService<PostgresWerkrDbContext>( );
                    ctx.FieldEncryption = sp.GetService<FieldEncryptionProvider>( );
                    return ctx;
                } );
                break;

            case DatabaseProvider.SQLite:
                _ = services.AddDbContext<SqliteWerkrDbContext>( options => {
                    _ = options.UseSqlite( connectionString )
                        .UseSnakeCaseNamingConvention( );
                } );
                _ = services.AddScoped<WerkrDbContext>( sp => {
                    SqliteWerkrDbContext ctx = sp.GetRequiredService<SqliteWerkrDbContext>( );
                    ctx.FieldEncryption = sp.GetService<FieldEncryptionProvider>( );
                    return ctx;
                } );
                break;

            default:
                throw new ArgumentOutOfRangeException( nameof( provider ), provider, "Unsupported database provider." );
        }

        return services;
    }
}
