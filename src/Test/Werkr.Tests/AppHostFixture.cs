using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using Werkr.Api;
using Werkr.Common;
using Werkr.Common.Auth;
using Werkr.Data;
using Werkr.Data.Identity;
using Werkr.Data.Identity.Entities;
using Werkr.Data.Identity.Roles;

namespace Werkr.Tests;

/// <summary>
/// Assembly-wide test fixture that starts a Postgres Testcontainer, boots the Werkr API
/// via <see cref="WebApplicationFactory{TEntryPoint}"/>, runs DB migrations, and exposes
/// a pre-authenticated <see cref="HttpClient"/>.
/// </summary>
[TestClass]
public static class AppHostFixture {
    private static PostgreSqlContainer? s_postgres;
    private static WebApplicationFactory<Program>? s_factory;

    /// <summary>Pre-built JSON options matching the API's camelCase convention.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new( JsonSerializerDefaults.Web );

    /// <summary>Authenticated HttpClient targeting the API. Carries an admin JWT.</summary>
    public static HttpClient ApiClient { get; private set; } = null!;

    [AssemblyInitialize]
    public static async Task InitializeAsync( TestContext testContext ) {
        CancellationToken ct = testContext.CancellationToken;

        // ── 1. Start a disposable Postgres container ─────────────────
        s_postgres = new PostgreSqlBuilder( "postgres:17-alpine" )
            .Build( );

        await s_postgres.StartAsync( ct );

        string connStr = s_postgres.GetConnectionString( );

        // ── 2. Create the in-process API server ──────────────────────
        s_factory = new WebApplicationFactory<Werkr.Api.Program>( )
            .WithWebHostBuilder( builder => {
                _ = builder.UseEnvironment( "Development" );

                // Provide settings that Program.Main reads before Build():
                //   ConnectionStrings:werkrdb, Jwt:SigningKey, Jwt:Issuer, Jwt:Audience
                _ = builder.UseSetting( "ConnectionStrings:werkrdb", connStr );
                _ = builder.UseSetting( "Jwt:SigningKey",
                    "werkr-dev-signing-key-do-not-use-in-production-min32chars!" );
                _ = builder.UseSetting( "Jwt:Issuer", "werkr-api" );
                _ = builder.UseSetting( "Jwt:Audience", "werkr" );

                _ = builder.ConfigureServices( services => {
                    // Remove the database context registrations added by Program.Main
                    // (which captured an empty connection string) and re-register
                    // them with the Testcontainer's connection string.
                    _ = services.RemoveAll( typeof( DbContextOptions<PostgresWerkrDbContext> ) );
                    _ = services.RemoveAll( typeof( DbContextOptions<WerkrDbContext> ) );
                    _ = services.RemoveAll( typeof( PostgresWerkrDbContext ) );
                    _ = services.RemoveAll( typeof( WerkrDbContext ) );

                    _ = services.RemoveAll( typeof( DbContextOptions<WerkrIdentityDbContext> ) );
                    _ = services.RemoveAll( typeof( DbContextOptions<PostgresWerkrIdentityDbContext> ) );
                    _ = services.RemoveAll( typeof( DbContextOptions<WerkrIdentityDbContext> ) );
                    _ = services.RemoveAll( typeof( PostgresWerkrIdentityDbContext ) );
                    _ = services.RemoveAll( typeof( WerkrIdentityDbContext ) );

                    _ = services.AddWerkrDbContext( DatabaseProvider.Postgres, connStr );

                    // Register Identity DbContext via provider-specific subclass +
                    // forwarding, matching the dual-provider pattern in AddWerkrIdentity.
                    _ = services.AddDbContext<PostgresWerkrIdentityDbContext>( options => {
                        _ = options.UseNpgsql( connStr, npgsql =>
                                npgsql.MigrationsHistoryTable( "__EFMigrationsHistory", "werkr_identity" ) )
                            .UseSnakeCaseNamingConvention( );
                    } );
                    _ = services.AddScoped<WerkrIdentityDbContext>( sp =>
                        sp.GetRequiredService<PostgresWerkrIdentityDbContext>( ) );

                    _ = services.AddIdentityCore<WerkrUser>(
                            Werkr.Data.Identity.Extensions.IdentityExtensions.ConfigureIdentityOptions )
                        .AddRoles<IdentityRole>( )
                        .AddEntityFrameworkStores<WerkrIdentityDbContext>( )
                        .AddDefaultTokenProviders( );
                } );
            } );

        // ── 3. Run EF Core migrations ────────────────────────────────
        using (IServiceScope scope = s_factory.Services.CreateScope( )) {
            WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
            await db.Database.MigrateAsync( ct );

            WerkrIdentityDbContext identityDb = scope.ServiceProvider
                .GetRequiredService<WerkrIdentityDbContext>( );
            await identityDb.Database.MigrateAsync( ct );
        }

        // ── 3b. Seed Identity roles & permissions ────────────────────
        //  The production IdentitySeeder lives in Werkr.Server, which
        //  has heavy dependencies (Blazor, SignalR, ServerConfigCache).
        //  Instead of pulling all of those in, replicate the minimal
        //  role/permission seed here so the permission-based auth
        //  pipeline resolves correctly.
        await SeedRolesAndPermissionsAsync( s_factory.Services, ct );

        // ── 4. Create an authenticated API client ────────────────────
        HttpClient apiClient = s_factory.CreateClient( );
        apiClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue( "Bearer", GenerateAdminJwt( ) );
        ApiClient = apiClient;
    }

    [AssemblyCleanup]
    public static async Task CleanupAsync( ) {
        ApiClient?.Dispose( );
        s_factory?.Dispose( );

        if (s_postgres is not null) {
            await s_postgres.DisposeAsync( );
            s_postgres = null;
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Identity seed — roles + role-permission mappings.
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Mirrors the role/permission seed from <c>Werkr.Server.Identity.IdentitySeeder</c>
    /// without dragging in the full Werkr.Server dependency graph.
    /// </summary>
    private static async Task SeedRolesAndPermissionsAsync(
        IServiceProvider services, CancellationToken ct ) {
        Dictionary<DefaultRoles, Permission[]> permissionMap = new( ) {
            [DefaultRoles.Admin] = [
                Permission.Create,
                Permission.Read,
                Permission.Update,
                Permission.Delete,
                Permission.Execute,
                Permission.Admin,
            ],
            [DefaultRoles.Operator] = [
                Permission.Read,
                Permission.Execute,
            ],
            [DefaultRoles.Viewer] = [
                Permission.Read,
            ],
        };

        using IServiceScope scope = services.CreateScope( );
        RoleManager<IdentityRole> roleManager = scope.ServiceProvider
            .GetRequiredService<RoleManager<IdentityRole>>( );
        WerkrIdentityDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<WerkrIdentityDbContext>( );

        // Create roles
        foreach (string role in Enum.GetNames<DefaultRoles>( )) {
            if (!await roleManager.RoleExistsAsync( role )) {
                _ = await roleManager.CreateAsync( new IdentityRole( role ) );
            }
        }

        // Create role-permission mappings
        foreach ((DefaultRoles defaultRole, Permission[] permissions) in permissionMap) {
            IdentityRole? role = await roleManager.FindByNameAsync( defaultRole.ToString( ) );
            if (role is null) {
                continue;
            }

            foreach (Permission permission in permissions) {
                bool exists = await dbContext.RolePermissions
                    .AnyAsync( rp => rp.RoleId == role.Id && rp.Permission == permission, ct );

                if (!exists) {
                    _ = dbContext.RolePermissions.Add( new RolePermission {
                        RoleId = role.Id,
                        Permission = permission,
                    } );
                }
            }
        }

        _ = await dbContext.SaveChangesAsync( ct );
    }

    // ──────────────────────────────────────────────────────────────────────
    //  JWT helper — mirrors the known dev-only signing config.
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generate a short-lived admin JWT signed with the development signing key.
    /// Matches the configuration in <c>Werkr.Api/appsettings.Development.json</c>.
    /// </summary>
    private static string GenerateAdminJwt( ) {
        const string SigningKey = "werkr-dev-signing-key-do-not-use-in-production-min32chars!";
        const string Issuer = "werkr-api";
        const string Audience = "werkr";

        SymmetricSecurityKey key = new( Encoding.UTF8.GetBytes( SigningKey ) );
        SigningCredentials credentials = new( key, SecurityAlgorithms.HmacSha256 );

        Claim[] claims = [
            new( ClaimTypes.NameIdentifier, Guid.NewGuid( ).ToString( ) ),
            new( ClaimTypes.Role, "Admin" ),
            new( WerkrClaimTypes.ApiKeyId, Guid.NewGuid( ).ToString( ) ),
            new( WerkrClaimTypes.ApiKeyName, "integration-test-key" ),
            new( JwtRegisteredClaimNames.Jti, Guid.NewGuid( ).ToString( ) ),
            // Permission claims required by ClaimsPermissionAuthorizationHandler
            new( WerkrClaimTypes.Permission, Permission.Create.ToString( ) ),
            new( WerkrClaimTypes.Permission, Permission.Read.ToString( ) ),
            new( WerkrClaimTypes.Permission, Permission.Update.ToString( ) ),
            new( WerkrClaimTypes.Permission, Permission.Delete.ToString( ) ),
            new( WerkrClaimTypes.Permission, Permission.Execute.ToString( ) ),
            new( WerkrClaimTypes.Permission, Permission.Admin.ToString( ) ),
        ];

        SecurityTokenDescriptor descriptor = new( ) {
            Issuer = Issuer,
            Audience = Audience,
            Subject = new ClaimsIdentity( claims ),
            Expires = DateTime.UtcNow.AddHours( 1 ),
            SigningCredentials = credentials,
        };

        return new JsonWebTokenHandler( ).CreateToken( descriptor );
    }
}
