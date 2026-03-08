using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Settings.Configuration;
using Werkr.Agent.Communication;
using Werkr.Agent.Interceptors;
using Werkr.Agent.Operators;
using Werkr.Agent.Registration;
using Werkr.Agent.Scheduling;
using Werkr.Agent.Security;
using Werkr.Agent.Services;
using Werkr.Common;
using Werkr.Common.Configuration;
using Werkr.Common.Extensions;
using Werkr.Common.Models;
using Werkr.Core.Cryptography;
using Werkr.Core.Operators;
using Werkr.Core.Security;
using Werkr.Core.Tasks;
using Werkr.Data;
using Werkr.ServiceDefaults;

namespace Werkr.Agent;

/// <summary>Application entry point for the Werkr Agent.</summary>
public class Program {
    private static readonly Random _random = new();

    /// <summary>Main entry point.</summary>
    /// <param name="args">Command-line arguments.</param>
    public static async Task Main( string[] args ) {
        Log.Logger = new LoggerConfiguration( )
            .WriteTo.Console( )
            .CreateBootstrapLogger( );

        try {
            Log.Information( "Starting Werkr Agent..." );

            string version = System.Reflection.CustomAttributeExtensions
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(
                    System.Reflection.Assembly.GetEntryAssembly( )! )
                ?.InformationalVersion ?? "unknown";
            Log.Information( "Werkr Agent version {Version}", version );

            // Validate platform crypto support
            EncryptionProvider.ValidatePlatformCryptoSupport( );

            // Retrieve or generate the SQLite passphrase from OS secret store
            ISecretStore secretStore = SecretStoreFactory.Create( );
            string? passphrase = await secretStore.GetSecretAsync( "werkr-agent-db" );
            if (passphrase is null) {
                passphrase = Convert.ToHexString( EncryptionProvider.GenerateRandomBytes( 32 ) );
                await secretStore.SetSecretAsync( "werkr-agent-db", passphrase );
                Log.Information( "Generated new SQLite passphrase for Agent database." );
            }

            // Determine platform-appropriate data directory
            string dataDir = GetDataDirectory( );
            _ = Directory.CreateDirectory( dataDir );
            string dbPath = Path.Combine( dataDir, "werkr-agent.db" );

            WebApplicationBuilder builder = WebApplication.CreateBuilder( args );

            _ = builder.Configuration.AddWerkrConfigPath( "Agent" );

            // Serilog (ConfigurationReaderOptions required for single-file publish)
            ConfigurationReaderOptions readerOptions = new(
                typeof( Serilog.ConsoleLoggerConfigurationExtensions ).Assembly,
                typeof( Serilog.FileLoggerConfigurationExtensions ).Assembly,
                typeof( Serilog.Sinks.OpenTelemetry.OtlpProtocol ).Assembly );
            _ = builder.Host.UseSerilog( ( ctx, lc ) => lc
                .ReadFrom.Configuration( ctx.Configuration, readerOptions ) );

            // Aspire service defaults
            _ = builder.AddServiceDefaults( );

            // Local SQLite WerkrDbContext
            string connectionString = $"Data Source={dbPath}";
            _ = builder.Services.AddWerkrDbContext( DatabaseProvider.SQLite, connectionString );

            // OS Secret Store
            _ = builder.Services.AddSingleton<ISecretStore>( secretStore );

            // Agent settings via options pattern
            _ = builder.Services.Configure<AgentSettings>(
                builder.Configuration.GetSection( AgentSettings.SectionName ) );
            _ = builder.Services.Configure<AllowedPathsConfiguration>(
                builder.Configuration.GetSection( AllowedPathsConfiguration.SectionName ) );
            _ = builder.Services.Configure<ActionOperatorConfiguration>(
                builder.Configuration.GetSection( ActionOperatorConfiguration.SectionName ) );

            // gRPC with BearerTokenInterceptor for authenticated access
            _ = builder.Services.AddGrpc( options => {
                options.Interceptors.Add<BearerTokenInterceptor>( );
            } );

            // Kestrel endpoint configuration.
            // All endpoints use TLS with Http1AndHttp2 — ALPN negotiates HTTP/2
            // for gRPC automatically. In containers, the TLS certificate is mounted
            // from the host and configured via ASPNETCORE_Kestrel__Certificates__Default__*
            // environment variables. Outside containers, the dev cert handles TLS.
            _ = builder.WebHost.ConfigureKestrel( options => {
                options.ConfigureEndpointDefaults( listenOptions => {
                    listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2;
                } );

                options.Limits.Http2.KeepAlivePingDelay = TimeSpan.FromSeconds( 30 );
                options.Limits.Http2.KeepAlivePingTimeout = TimeSpan.FromSeconds( 10 );
            } );

            // Agent-specific services
            _ = builder.Services.AddSingleton<PwshOperator>( );
            _ = builder.Services.AddSingleton<SystemShellOperator>( );
            _ = builder.Services.AddSingleton<IActionOperator, ActionOperator>( );
            _ = builder.Services.AddActionHandlers( );
            _ = builder.Services.AddSingleton<IPathAllowlistValidator, PathAllowlistValidator>( );
            _ = builder.Services.AddSingleton<IFilePathResolver, FilePathResolver>( );
            _ = builder.Services.AddScoped<AgentRegistrationHandler>( );

            // Schedule evaluation services
            _ = builder.Services.AddSingleton<AgentGrpcClientFactory>( );
            _ = builder.Services.Configure<JobOutputOptions>(
                builder.Configuration.GetSection( JobOutputOptions.SectionName ) );
            _ = builder.Services.AddSingleton<AgentJobOutputWriter>( );
            _ = builder.Services.AddSingleton<SuccessCriteriaEvaluator>( );
            _ = builder.Services.AddSingleton( Channel.CreateUnbounded<string>(
                new UnboundedChannelOptions { SingleReader = true } ) );
            _ = builder.Services.AddHostedService<ScheduleEvaluatorService>( );

            WebApplication app = builder.Build( );

            // Auto-migrate in development
            if (app.Environment.IsDevelopment( )) {
                using IServiceScope scope = app.Services.CreateScope( );
                WerkrDbContext dbContext = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

                // Clear any stale EF Core migration lock left by a previous crash/kill.
                // EF Core 9+ uses __EFMigrationsLock for distributed locking, but for a
                // single-process SQLite database this lock can only become stale
                // (no second process will ever release it). Without this cleanup,
                // MigrateAsync spins on INSERT OR IGNORE indefinitely.
                try {
                    _ = await dbContext.Database.ExecuteSqlRawAsync(
                        "DELETE FROM \"__EFMigrationsLock\"" );
                } catch {
                    // Table may not exist on first run — safe to ignore.
                }

                await dbContext.Database.MigrateAsync( );
            }

            // Map gRPC services
            _ = app.MapGrpcService<PwshService>( );
            _ = app.MapGrpcService<SystemShellService>( );
            _ = app.MapGrpcService<ActionService>( );
            _ = app.MapGrpcService<OutputFetchService>( );
            _ = app.MapGrpcService<ScheduleInvalidationService>( );
            _ = app.MapGrpcService<ConnectionManagementService>( );

            // Default Aspire endpoints (health, etc.)
            _ = app.MapDefaultEndpoints( );

            // Registration endpoints (localhost-only)
            _ = app.MapRegistrationEndpoints( );

            _ = app.MapGet( "/", GetAgentArt );

            await app.RunAsync( );
        } catch (Exception ex) {
            Log.Fatal( ex, "Werkr Agent terminated unexpectedly." );
        } finally {
            await Log.CloseAndFlushAsync( );
        }
    }

    private static IResult GetAgentArt( ) {
        const string asciiArt = """
    ╔════════════════════════════════╗
    ║ ┌────────────────────────────┐ ║
    ║ │      ---          ---      │ ║
    ║ │       •            •       │ ║
╔═══║ │   ______________________   │ ║═══╗
║   ║ └────────────────────────────┘ ║   ║
║   ║ __        __        _          ║   ║
║   ║ \ \      / /__ _ __| | ___ __  ║   ║
║   ║  \ \ /\ / / _ \ '__| |/ / '__| ║   ║
║___║   \ V  V /  __/ |  |   <| |    ║___║
    ║    \_/\_/ \___|_|  |_|\_\_|    ║
    ╚════════════════════════════════╝
            |AGENT|     | gRPC|
    ++++++++++++++++++++++++++++++++++
""";

        const string happyAsciiArt = """
      ╔════════════════════════════════╗
      ║ ┌────────────────────────────┐ ║
      ║ │      ---          ---      │ ║
      ║ │       •            •       │ ║                              ,--,
 ╔════║ │   \____________________/   │ ║════╗                         \ /
 ║    ║ └────────────────────────────┘ ║    ║                        {|||)<
 ║    ║ __        __        _          ║    ║                         / \
 ║    ║ \ \      / /__ _ __| | ___ __  ║    ║                         `--`
 ║    ║  \ \ /\ / / _ \ '__| |/ / '__| ║    ║
 ║____║   \ V  V /  __/ |  |   <| |    ║____║    \|/    \|/    \|/    \|/    \|/    \|/    \|/
      ║    \_/\_/ \___|_|  |_|\_\_|    ║        --*--  --*--  --*--  --*--  --*--  --*--  --*--
      ╚════════════════════════════════╝          |      |      |      |      |      |      |
              |AGENT|     | gRPC|                 |      |      |      |      |      |      |
++++++++++++++++++++++++++++++++++++++++._______._|_.__._|_.__._|_.__._|_.__._|_.__._|_.__._|_.
""";
        return Results.Text(
            content: _random.Next( 0, 10 ) == 0
                ? happyAsciiArt
                : asciiArt,
            contentType: "text/plain; charset=utf-8"
        );
    }

    /// <summary>
    /// Returns the platform-appropriate data directory for the Agent.
    /// Checks the WERKR_DATA_DIR environment variable first, then falls back
    /// to platform-specific defaults.
    /// </summary>
    /// <returns>The absolute path to the data directory.</returns>
    private static string GetDataDirectory( ) {
        // Environment variable override (e.g. Docker containers, systemd services)
        string? envDataDir = Environment.GetEnvironmentVariable( "WERKR_DATA_DIR" );
        if (!string.IsNullOrWhiteSpace( envDataDir )) {
            return envDataDir;
        }

        if (RuntimeInformation.IsOSPlatform( OSPlatform.Windows )) {
            string localAppData = Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData );
            return Path.Combine( localAppData, "Werkr", "data" );
        }

        if (RuntimeInformation.IsOSPlatform( OSPlatform.OSX )) {
            string home = Environment.GetFolderPath( Environment.SpecialFolder.UserProfile );
            return Path.Combine( home, "Library", "Application Support", "Werkr", "data" );
        }

        // Linux
        string dataHome = Environment.GetEnvironmentVariable( "XDG_DATA_HOME" )
            ?? Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ), ".local", "share" );
        return Path.Combine( dataHome, "werkr", "data" );
    }
}
