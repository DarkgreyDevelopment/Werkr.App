using System.Reflection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Settings.Configuration;
using Werkr.Api.Authorization;
using Werkr.Api.Endpoints;
using Werkr.Api.Interceptors;
using Werkr.Api.Services;
using Werkr.Common;
using Werkr.Common.Auth;
using Werkr.Common.Configuration;
using Werkr.Common.Extensions;
using Werkr.Core.Communication;
using Werkr.Core.Cryptography;
using Werkr.Core.Health;
using Werkr.Core.Registration;
using Werkr.Core.Scheduling;
using Werkr.Core.Tasks;
using Werkr.Data;
using Werkr.Data.Seeding;
using Werkr.ServiceDefaults;

namespace Werkr.Api;

/// <summary>Application entry point for the Werkr API service.</summary>
public class Program {
    /// <summary>Main entry point.</summary>
    /// <param name="args">Command-line arguments.</param>
    public static async Task Main( string[] args ) {
        Log.Logger = new LoggerConfiguration( )
            .WriteTo.Console( )
            .CreateBootstrapLogger( );

        try {
            Log.Information( "Starting Werkr API Service..." );

            string version = CustomAttributeExtensions
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>(
                    Assembly.GetEntryAssembly( )! )
                ?.InformationalVersion ?? "unknown";
            Log.Information( "Werkr API version {Version}", version );

            // Platform validation gate — fail fast if crypto is not supported
            EncryptionProvider.ValidatePlatformCryptoSupport( );
            Log.Information( "Platform cryptographic validation passed." );

            WebApplicationBuilder builder = WebApplication.CreateBuilder( args );

            _ = builder.Configuration.AddWerkrConfigPath( "Api" );

            // Serilog (ConfigurationReaderOptions required for single-file publish)
            ConfigurationReaderOptions readerOptions = new(
                typeof( Serilog.ConsoleLoggerConfigurationExtensions ).Assembly,
                typeof( Serilog.FileLoggerConfigurationExtensions ).Assembly,
                typeof( Serilog.Sinks.OpenTelemetry.OtlpProtocol ).Assembly );
            _ = builder.Host.UseSerilog( ( ctx, lc ) => lc
                .ReadFrom.Configuration( ctx.Configuration, readerOptions ) );

            // Add service defaults & Aspire client integrations.
            _ = builder.AddServiceDefaults( );

            // Add services to the container.
            _ = builder.Services.AddProblemDetails( );

            // OpenAPI
            _ = builder.Services.AddOpenApi( );

            // gRPC with Agent bearer-token validation
            _ = builder.Services.AddGrpc( options => {
                options.Interceptors.Add<AgentBearerTokenInterceptor>( );
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
            } );

            // Database — provider is configurable via Database:Provider (default: Postgres)
            string connectionString = builder.Configuration.GetConnectionString( "werkrdb" ) ?? string.Empty;
            DatabaseProvider dbProvider = Enum.TryParse<DatabaseProvider>(
                builder.Configuration["Database:Provider"], ignoreCase: true, out DatabaseProvider parsed )
                ? parsed : DatabaseProvider.Postgres;
            _ = builder.Services.AddWerkrDbContext( dbProvider, connectionString );

            // Configuration
            WerkrConfiguration werkrConfig = new( );
            builder.Configuration.GetSection( WerkrConfiguration.SectionName ).Bind( werkrConfig );

            // JWT Bearer Authentication (validation only — Server issues tokens)
            _ = builder.Services.AddAuthentication( options => {
                options.DefaultAuthenticateScheme =
                    Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme =
                    Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
            } )
                .AddJwtBearer( options => {
                    options.TokenValidationParameters =
                        JwtValidationConfigurator.GetParameters( builder.Configuration );
                } );

            // Permission-based authorization (claims-based handler — no identity DB queries)
            _ = builder.Services.AddAuthorization( options => options.AddWerkrPermissionPolicies( ) );
            _ = builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
                ClaimsPermissionAuthorizationHandler>( );

            // Named HttpClient for proxying token requests to the Server
            _ = builder.Services.AddHttpClient( "ServerService", client => {
                client.BaseAddress = new Uri( "https://server" );
            } );

            // Registration service
            // ServerUrl may be set explicitly in config; if not, resolve from the running server's addresses at runtime.
            _ = builder.Services.AddScoped<RegistrationService>( sp => {
                WerkrDbContext dbContext = sp.GetRequiredService<WerkrDbContext>( );
                ILogger<RegistrationService> logger = sp.GetRequiredService<ILogger<RegistrationService>>( );
                string serverUrl = werkrConfig.ServerUrl;
                if (string.IsNullOrWhiteSpace( serverUrl )) {
                    IServer server = sp.GetRequiredService<IServer>( );
                    IServerAddressesFeature? addressesFeature = server.Features.Get<IServerAddressesFeature>( );
                    serverUrl = addressesFeature?.Addresses
                        .FirstOrDefault( a => a.StartsWith( "https://", StringComparison.OrdinalIgnoreCase ) )
                        ?? addressesFeature?.Addresses.FirstOrDefault( )
                        ?? throw new InvalidOperationException(
                            "Werkr:ServerUrl is not configured and no server addresses are available. " +
                            "Set 'Werkr:ServerUrl' in appsettings.json or ensure the server has bound addresses." );
                }
                return new RegistrationService( dbContext, logger, serverUrl );
            } );

            // Bundle expiration background service
            _ = builder.Services.AddHostedService<BundleExpirationService>( sp => {
                IServiceScopeFactory scopeFactory = sp.GetRequiredService<IServiceScopeFactory>( );
                ILogger<BundleExpirationService> logger = sp.GetRequiredService<ILogger<BundleExpirationService>>( );
                return new BundleExpirationService( scopeFactory, logger );
            } );

            // Agent connection manager (Singleton — caches gRPC channels)
            _ = builder.Services.AddSingleton<AgentConnectionManager>( );

            // Job event broadcaster (Singleton — SSE fan-out for real-time push)
            _ = builder.Services.AddSingleton<JobEventBroadcaster>( );

            // Workflow event broadcaster (Singleton — SSE fan-out for step lifecycle events)
            _ = builder.Services.AddSingleton<WorkflowEventBroadcaster>( );

            // Output streaming gRPC service (Singleton — receives agent output streams)
            _ = builder.Services.AddSingleton<OutputStreamingGrpcService>( );

            // Agent health check background service — keeps DB status current
            _ = builder.Services.AddHostedService<AgentHealthCheckService>( sp => {
                IServiceScopeFactory scopeFactory = sp.GetRequiredService<IServiceScopeFactory>( );
                AgentConnectionManager connectionManager = sp.GetRequiredService<AgentConnectionManager>( );
                ILogger<AgentHealthCheckService> logger =
                    sp.GetRequiredService<ILogger<AgentHealthCheckService>>( );
                return new AgentHealthCheckService( scopeFactory, connectionManager, logger );
            } );

            // Schedule service (Scoped — one per request)
            _ = builder.Services.AddScoped<ScheduleService>( );
            _ = builder.Services.AddScoped<RunNowService>( );
            _ = builder.Services.AddScoped<RetryFromFailedService>( );

            // Task & Job services (Scoped — one per request)
            _ = builder.Services.Configure<JobOutputOptions>(
                builder.Configuration.GetSection( JobOutputOptions.SectionName ) );
            _ = builder.Services.Configure<WorkflowVariableOptions>(
                builder.Configuration.GetSection( WorkflowVariableOptions.SectionName ) );
            _ = builder.Services.AddScoped<TaskService>( );
            _ = builder.Services.AddScoped<AgentResolver>( );
            _ = builder.Services.AddScoped<JobOutputWriter>( );
            _ = builder.Services.AddScoped<SuccessCriteriaEvaluator>( );
            _ = builder.Services.AddScoped<JobExecutionService>( );

            // Workflow services (Scoped — one per request)
            _ = builder.Services.AddScoped<Werkr.Core.Workflows.ConditionEvaluator>( );
            _ = builder.Services.AddScoped<Werkr.Core.Workflows.WorkflowService>( );

            // Schedule invalidation dispatcher (Scoped — sends push notifications to agents)
            _ = builder.Services.AddScoped<ScheduleInvalidationDispatcher>( );

            // Holiday calendar services (Scoped — one per request)
            _ = builder.Services.AddScoped<HolidayDateService>( );
            _ = builder.Services.AddScoped<HolidayCalendarService>( );

            // Audit log cleanup
            _ = builder.Services.Configure<AuditLogOptions>( builder.Configuration.GetSection( "AuditLog" ) );
            _ = builder.Services.AddHostedService<AuditLogCleanupService>( );

            // Key rotation background service — rotates SharedKey for all connected agents
            _ = builder.Services.AddSingleton<KeyRotationService>( sp => {
                IServiceScopeFactory scopeFactory = sp.GetRequiredService<IServiceScopeFactory>( );
                AgentConnectionManager connectionManager = sp.GetRequiredService<AgentConnectionManager>( );
                ILogger<KeyRotationService> logger =
                    sp.GetRequiredService<ILogger<KeyRotationService>>( );
                return new KeyRotationService( scopeFactory, connectionManager, logger );
            } );
            _ = builder.Services.AddHostedService( sp => sp.GetRequiredService<KeyRotationService>( ) );

            WebApplication app = builder.Build( );

            // Auto-migrate app DB in development (API owns the application database)
            if (app.Environment.IsDevelopment( )) {
                using IServiceScope scope = app.Services.CreateScope( );
                WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
                await db.Database.MigrateAsync( );
            }

            // Seed system holiday calendars
            await HolidayCalendarSeeder.SeedAsync( app.Services );

            // Configure the HTTP request pipeline.
            _ = app.UseExceptionHandler( );

            if (app.Environment.IsDevelopment( )) {
                _ = app.MapOpenApi( );
            }

            // Authentication & Authorization middleware
            _ = app.UseAuthentication( );
            _ = app.UseAuthorization( );

            // gRPC services
            _ = app.MapGrpcService<RegistrationGrpcService>( );
            _ = app.MapGrpcService<ScheduleSyncGrpcService>( );
            _ = app.MapGrpcService<JobReportingGrpcService>( );
            _ = app.MapGrpcService<OutputStreamingGrpcService>( );
            _ = app.MapGrpcService<VariableGrpcService>( );

            // REST endpoints
            _ = app.MapStatusEndpoints( );
            _ = app.MapAuthProxyEndpoints( );
            _ = app.MapRegistrationEndpoints( );
            _ = app.MapAgentEndpoints( );
            _ = app.MapDiagnosticsEndpoints( );
            _ = app.MapScheduleEndpoints( );
            _ = app.MapTaskEndpoints( );
            _ = app.MapJobEndpoints( );
            _ = app.MapSettingsEndpoints( );
            _ = app.MapWorkflowEndpoints( );
            _ = app.MapVariableEndpoints( );
            _ = app.MapHolidayCalendarEndpoints( );
            _ = app.MapEventEndpoints( );
            _ = app.MapShellEndpoints( );
            _ = app.MapFilterEndpoints( );

            _ = app.MapDefaultEndpoints( );

            app.Run( );
        } catch (Exception ex) {
            Log.Fatal( ex, "Werkr API Service terminated unexpectedly." );
        } finally {
            Log.CloseAndFlush( );
        }
    }
}
