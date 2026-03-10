using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Settings.Configuration;
using Werkr.Common;
using Werkr.Common.Auth;
using Werkr.Common.Extensions;
using Werkr.Data.Identity;
using Werkr.Data.Identity.Authorization;
using Werkr.Data.Identity.Extensions;
using Werkr.Data.Identity.Services;
using Werkr.Server.Components;
using Werkr.Server.Endpoints;
using Werkr.Server.Identity;
using Werkr.Server.Services;
using Werkr.ServiceDefaults;

namespace Werkr.Server;

/// <summary>Application entry point for the Werkr Server (Blazor UI).</summary>
public class Program {
    /// <summary>Main entry point.</summary>
    /// <param name="args">Command-line arguments.</param>
    public static async Task Main( string[] args ) {
        Log.Logger = new LoggerConfiguration( )
            .WriteTo.Console( )
            .CreateBootstrapLogger( );

        try {
            Log.Information( "Starting Werkr Server..." );

            string version = System.Reflection.CustomAttributeExtensions
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(
                    System.Reflection.Assembly.GetEntryAssembly()!
                )
                ?.InformationalVersion ?? "unknown";
            Log.Information( "Werkr Server version {Version}", version );

            WebApplicationBuilder builder = WebApplication.CreateBuilder( args );

            _ = builder.Configuration.AddWerkrConfigPath( "Server" );

            // Serilog (ConfigurationReaderOptions required for single-file publish)
            ConfigurationReaderOptions readerOptions = new(
                typeof( ConsoleLoggerConfigurationExtensions ).Assembly,
                typeof( FileLoggerConfigurationExtensions ).Assembly,
                typeof(Serilog.Sinks.OpenTelemetry.OtlpProtocol).Assembly
            );
            _ = builder.Host.UseSerilog( ( ctx, lc ) => lc
                .ReadFrom.Configuration( ctx.Configuration, readerOptions ) );

            // Add service defaults & Aspire client integrations.
            _ = builder.AddServiceDefaults( );

            // Data Protection — persist keys so authenticator tokens survive container restarts
            DirectoryInfo keysDir = new( Path.Combine( builder.Environment.ContentRootPath, "keys" ) );
            _ = builder.Services.AddDataProtection( )
                .SetApplicationName( "Werkr" )
                .PersistKeysToFileSystem( keysDir );

            // Identity (uses separate identity database) — provider is configurable via Database:Provider
            string connectionString = builder.Configuration.GetConnectionString( "werkridentitydb" ) ?? string.Empty;
            DatabaseProvider dbProvider = Enum.TryParse<DatabaseProvider>(
                builder.Configuration["Database:Provider"], ignoreCase: true, out DatabaseProvider parsed
            )
                ? parsed : DatabaseProvider.Postgres;
            _ = builder.Services.AddWerkrIdentity( dbProvider, connectionString );

            // Permission system (role-permission mapping, authorization policies)
            _ = builder.Services.AddScoped<IPermissionService, PermissionService>( );
            _ = builder.Services.AddAuthorization( options => options.AddWerkrPermissionPolicies( ) );
            _ = builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
                PermissionAuthorizationHandler>( );

            // JWT token service — Server is the sole JWT issuer (Decision A1)
            _ = builder.Services.AddSingleton<JwtTokenService>( sp => {
                IConfiguration config = sp.GetRequiredService<IConfiguration>( );
                ILogger<JwtTokenService> logger = sp.GetRequiredService<ILogger<JwtTokenService>>( );
                return new JwtTokenService( config, logger );
            } );

            // API key service — Server owns the identity DB (Decision A1)
            _ = builder.Services.AddScoped<ApiKeyService>( );

            // Configure auth cookie paths
            _ = builder.Services.ConfigureApplicationCookie( options => {
                options.LoginPath = "/account/login";
                options.AccessDeniedPath = "/account/access-denied";
                options.LogoutPath = "/account/logout";
                options.EventsType = typeof( WerkrCookieAuthEvents );
                options.ExpireTimeSpan = TimeSpan.FromMinutes( 30 );
                options.SlidingExpiration = true;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.Cookie.SameSite = SameSiteMode.Strict;
            } );

            _ = builder.Services.AddScoped<WerkrCookieAuthEvents>( );
            _ = builder.Services.AddHttpContextAccessor( );

            // Make auth state available to interactive server circuits (not just SSR)
            _ = builder.Services.AddCascadingAuthenticationState( );

            // Add services to the container.
            _ = builder.Services.AddRazorComponents( )
                .AddInteractiveServerComponents( options => {
                    options.JSInteropDefaultCallTimeout = TimeSpan.FromSeconds( 60 );
                    options.DetailedErrors = builder.Environment.IsDevelopment( );
                    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes( 3 );
                } );

            _ = builder.Services.AddOutputCache( );

            // Server configuration cache — reads config from the DB instead of appsettings
            _ = builder.Services.AddSingleton<ServerConfigCache>( );

            // Auth forwarding handler — self-mints JWT for outgoing API requests
            _ = builder.Services.AddTransient<AuthForwardingHandler>( );

            // General-purpose HttpClient for the API service via service discovery
            _ = builder.Services.AddHttpClient( "ApiService", client => {
                client.BaseAddress = new Uri( "https://api" );
            } )
            .AddHttpMessageHandler<AuthForwardingHandler>( );

            // Background health monitor — keeps agent DB status in sync with actual reachability
            _ = builder.Services.AddHostedService<AgentHealthMonitorService>( );

            WebApplication app = builder.Build( );

            // Auto-migrate identity DB in development
            if (app.Environment.IsDevelopment( )) {
                using IServiceScope scope = app.Services.CreateScope( );

                WerkrIdentityDbContext identityDb = scope.ServiceProvider
                    .GetRequiredService<WerkrIdentityDbContext>( );
                await identityDb.Database.MigrateAsync( );
            }

            // Initialize the server config cache from the database
            ServerConfigCache configCache = app.Services.GetRequiredService<ServerConfigCache>( );
            await configCache.InitializeAsync( );

            // Seed default roles and admin account
            await IdentitySeeder.SeedAsync( app.Services );

            if (!app.Environment.IsDevelopment( )) {
                _ = app.UseExceptionHandler( "/Error", createScopeForErrors: true );
                _ = app.UseHsts( );
            }

            _ = app.UseHttpsRedirection( );

            // Authentication & Authorization middleware (BEFORE MapRazorComponents)
            _ = app.UseAuthentication( );
            _ = app.UseAuthorization( );

            _ = app.UseAntiforgery( );

            _ = app.UseOutputCache( );

            _ = app.MapStaticAssets( );

            _ = app.MapRazorComponents<App>( )
                .AddInteractiveServerRenderMode( );

            _ = app.MapDefaultEndpoints( );

            // Auth endpoints — token exchange and API key management (Decision A1)
            _ = app.MapAuthEndpoints( );

            app.Run( );
        } catch (Exception ex) {
            Log.Fatal( ex, "Werkr Server terminated unexpectedly." );
        } finally {
            Log.CloseAndFlush( );
        }
    }
}
