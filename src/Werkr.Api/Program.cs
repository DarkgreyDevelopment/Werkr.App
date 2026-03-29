using System.Reflection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Settings.Configuration;
using Serilog.Sinks.OpenTelemetry;
using Werkr.Api.Authorization;
using Werkr.Api.Endpoints;
using Werkr.Api.Interceptors;
using Werkr.Api.Services;
using Werkr.Common;
using Werkr.Common.Auth;
using Werkr.Common.Configuration;
using Werkr.Common.Extensions;
using Werkr.Core.Audit;
using Werkr.Core.Communication;
using Werkr.Core.Configuration;
using Werkr.Core.Credentials;
using Werkr.Core.Cryptography;
using Werkr.Core.Health;
using Werkr.Core.Notifications;
using Werkr.Core.Notifications.Channels;
using Werkr.Core.Registration;
using Werkr.Core.Retention;
using Werkr.Core.Retention.Providers;
using Werkr.Core.Scheduling;
using Werkr.Core.Security;
using Werkr.Core.Tasks;
using Werkr.Core.Triggers;
using Werkr.Core.Workflows;
using Werkr.Data;
using Werkr.Data.Encryption;
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
                typeof(ConsoleLoggerConfigurationExtensions).Assembly,
                typeof(FileLoggerConfigurationExtensions).Assembly,
                typeof(OtlpProtocol).Assembly);
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
                    listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                } );
            } );

            // Database — provider is configurable via Database:Provider (default: Postgres)
            string connectionString = builder.Configuration.GetConnectionString( "werkrdb" ) ?? string.Empty;
            DatabaseProvider dbProvider = Enum.TryParse<DatabaseProvider>(
                builder.Configuration["Database:Provider"],
                ignoreCase: true,
                out DatabaseProvider parsed
            )
                ? parsed
                : DatabaseProvider.Postgres;

            _ = builder.Services.AddWerkrDbContext( dbProvider, connectionString );

            // Field-level encryption — transparently encrypts sensitive DB columns
            ISecretStore apiSecretStore = SecretStoreFactory.Create( );
            _ = builder.Services.AddSingleton( apiSecretStore );
            string? fieldEncryptionKey = await apiSecretStore.GetSecretAsync(
                FieldEncryptionProvider.SecretStoreKey );
            if (fieldEncryptionKey is null) {
                fieldEncryptionKey = FieldEncryptionProvider.GenerateKey( );
                await apiSecretStore.SetSecretAsync(
                    FieldEncryptionProvider.SecretStoreKey, fieldEncryptionKey );
                Log.Information( "Generated new field encryption key for API database." );
            }
            FieldEncryptionProvider fieldEncryption = new( fieldEncryptionKey );
            _ = builder.Services.AddSingleton( fieldEncryption );

            // Configuration
            WerkrConfiguration werkrConfig = new( );
            builder.Configuration.GetSection( WerkrConfiguration.SectionName ).Bind( werkrConfig );

            // JWT Bearer Authentication (validation only — Server issues tokens)
            // Agent gRPC calls authenticate via AgentBearerTokenInterceptor (API-key hash),
            // not JWT. Skip JWT validation for agent requests to avoid SecurityTokenMalformedException noise.
            _ = builder.Services.AddAuthentication( options => {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            } )
                .AddJwtBearer( options => {
                    options.TokenValidationParameters =
                        JwtValidationConfigurator.GetParameters( builder.Configuration );
                    options.Events = new JwtBearerEvents {
                        OnMessageReceived = context => {
                            // Agent gRPC requests include x-werkr-connection-id; skip JWT parsing for those.
                            if (context.Request.Headers.ContainsKey( "x-werkr-connection-id" )) {
                                context.NoResult( );
                            }
                            return Task.CompletedTask;
                        },
                    };
                } );

            // Permission-based authorization (claims-based handler — no identity DB queries)
            _ = builder.Services.AddAuthorization( options => options.AddWerkrPermissionPolicies( ) );
            _ = builder.Services.AddSingleton<IAuthorizationHandler,
                ClaimsPermissionAuthorizationHandler>( );

            // Named HttpClient for proxying token requests to the Server
            _ = builder.Services.AddHttpClient( "ServerService", client => {
                client.BaseAddress = new Uri( "https://server" );
            } );

            // Named HttpClient for webhook notification delivery
            _ = builder.Services.AddHttpClient( "WerkrNotifications" );

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

            // Agent staleness detection background service — marks stale agents offline and cleans expired notifications
            _ = builder.Services.AddHostedService<AgentStalenessService>( sp => {
                IServiceScopeFactory scopeFactory = sp.GetRequiredService<IServiceScopeFactory>( );
                ILogger<AgentStalenessService> logger =
                    sp.GetRequiredService<ILogger<AgentStalenessService>>( );
                return new AgentStalenessService( scopeFactory, logger );
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
            _ = builder.Services.AddScoped<TaskVersionService>( );
            _ = builder.Services.AddScoped<TaskVersionDiffService>( );
            _ = builder.Services.AddScoped<TaskService>( );
            _ = builder.Services.AddScoped<AgentResolver>( );
            _ = builder.Services.AddScoped<JobOutputWriter>( );
            _ = builder.Services.AddScoped<SuccessCriteriaEvaluator>( );
            _ = builder.Services.AddScoped<JobExecutionService>( );

            // Workflow services (Scoped — one per request)
            _ = builder.Services.AddScoped<ConditionEvaluator>( );
            _ = builder.Services.AddScoped<WorkflowVersionService>( );
            _ = builder.Services.AddScoped<WorkflowVersionDiffService>( );
            _ = builder.Services.AddScoped<WorkflowService>( );

            // Trigger versioning service (Scoped)
            _ = builder.Services.AddScoped<TriggerVersionService>( );

            // Agent notification outbox (Scoped — participates in caller's transaction)
            _ = builder.Services.AddScoped<AgentNotificationService>( );

            // Secure gRPC response builder (Singleton — creates scoped DbContext for outbox checks)
            _ = builder.Services.AddSingleton<SecureResponseBuilder>( );

            // Configuration resolution service (Scoped)
            _ = builder.Services.AddScoped<IConfigurationResolutionService, ConfigurationResolutionService>( );
            _ = builder.Services.AddScoped<ConfigurationChangeNotifier>( );

            // Credential service (Scoped)
            _ = builder.Services.AddScoped<ICredentialService, CredentialService>( );

            // Schedule invalidation dispatcher (Scoped — sends push notifications to agents)
            _ = builder.Services.AddScoped<ScheduleInvalidationDispatcher>( );

            // Workflow disabled dispatcher (Scoped — notifies agents when a workflow is disabled)
            _ = builder.Services.AddScoped<WorkflowDisabledDispatcher>( );

            // Holiday calendar services (Scoped — one per request)
            _ = builder.Services.AddScoped<HolidayDateService>( );
            _ = builder.Services.AddScoped<HolidayCalendarService>( );

            // Audit event system
            AuditEventTypeRegistry auditRegistry = new( );
            _ = auditRegistry.RegisterCoreAuditEvents( );
            _ = builder.Services.AddSingleton<IAuditEventTypeRegistry>( auditRegistry );
            _ = builder.Services.AddScoped<IAuditService, AuditService>( );

            // Notification event category registry
            NotificationEventCategoryRegistry notificationEventRegistry = new( );
            _ = notificationEventRegistry.RegisterCoreNotificationEvents( );
            _ = builder.Services.AddSingleton<INotificationEventCategoryRegistry>( notificationEventRegistry );

            // Notification channel implementations (multi-registration for channel resolver)
            _ = builder.Services.AddScoped<INotificationChannel, EmailNotificationChannel>( );
            _ = builder.Services.AddScoped<INotificationChannel, WebhookNotificationChannel>( sp => {
                WerkrDbContext db = sp.GetRequiredService<WerkrDbContext>( );
                ILogger<WebhookNotificationChannel> log = sp.GetRequiredService<ILogger<WebhookNotificationChannel>>();
                IHttpClientFactory httpFactory = sp.GetRequiredService<IHttpClientFactory>( );
                HttpClient httpClient = httpFactory.CreateClient( "WerkrNotifications" );
                return new WebhookNotificationChannel( httpClient, db, log );
            } );
            _ = builder.Services.AddScoped<INotificationChannel, InAppNotificationChannel>( );
            _ = builder.Services.AddSingleton<INotificationHubService, NullNotificationHubService>( );

            // Notification delivery pipeline
            _ = builder.Services.AddScoped<NotificationChannelResolver>( );
            _ = builder.Services.AddScoped<INotificationDeliveryService, NotificationDeliveryService>( );

            // Notification retry background service
            _ = builder.Services.AddSingleton<NotificationRetryService>( sp => {
                IServiceScopeFactory scopeFactory = sp.GetRequiredService<IServiceScopeFactory>( );
                ILogger<NotificationRetryService> retryLogger = sp.GetRequiredService<ILogger<NotificationRetryService>>();
                return new NotificationRetryService( scopeFactory, retryLogger );
            } );
            _ = builder.Services.AddHostedService( sp => sp.GetRequiredService<NotificationRetryService>( ) );

            // Retention framework — policy-driven data lifecycle management
            RetentionPolicyRegistry retentionRegistry = new( );
            _ = builder.Services.AddSingleton( retentionRegistry );
            _ = builder.Services.AddScoped<IRetentionPolicyProvider, WorkflowRunRetentionProvider>( );
            _ = builder.Services.AddScoped<IRetentionPolicyProvider, AuditLogRetentionProvider>( );
            _ = builder.Services.AddScoped<IRetentionPolicyProvider, JobOutputRetentionProvider>( );
            _ = builder.Services.AddScoped<IRetentionPolicyProvider, WorkflowRunVariableRetentionProvider>( );
            _ = builder.Services.AddScoped<IRetentionPolicyProvider, NotificationDeliveryRetentionProvider>( );
            _ = builder.Services.AddScoped<IRetentionPolicyProvider, UserNotificationRetentionProvider>( );
            _ = builder.Services.AddSingleton<RetentionService>( sp => {
                IServiceScopeFactory scopeFactory = sp.GetRequiredService<IServiceScopeFactory>( );
                ILogger<RetentionService> retentionLogger = sp.GetRequiredService<ILogger<RetentionService>>( );

                // Register providers into the registry at startup
                using IServiceScope providerScope = scopeFactory.CreateScope( );
                foreach (IRetentionPolicyProvider provider in providerScope.ServiceProvider.GetServices<IRetentionPolicyProvider>( )) {
                    retentionRegistry.Register( provider );
                }

                return new RetentionService( scopeFactory, retentionRegistry, retentionLogger );
            } );
            _ = builder.Services.AddHostedService( sp => sp.GetRequiredService<RetentionService>( ) );

            // Key rotation background service — two-phase rotation via notification outbox
            _ = builder.Services.AddSingleton<KeyRotationService>( sp => {
                IServiceScopeFactory scopeFactory = sp.GetRequiredService<IServiceScopeFactory>( );
                ILogger<KeyRotationService> logger =
                    sp.GetRequiredService<ILogger<KeyRotationService>>( );
                TimeSpan gracePeriod = TimeSpan.FromMinutes( werkrConfig.KeyRotationGracePeriodMinutes );
                return new KeyRotationService( scopeFactory, logger,
                    gracePeriod: gracePeriod );
            } );
            _ = builder.Services.AddHostedService( sp => sp.GetRequiredService<KeyRotationService>( ) );

            // Field-level encryption key rotation service (§9 key rotation with zero-downtime re-encryption)
            _ = builder.Services.AddSingleton<Core.Encryption.IFieldEncryptionKeyRotationService,
                Core.Encryption.FieldEncryptionKeyRotationService>( );

            WebApplication app = builder.Build( );

            // Auto-migrate app DB in development (API owns the application database)
            if (app.Environment.IsDevelopment( )) {
                using IServiceScope scope = app.Services.CreateScope( );
                WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
                await db.Database.MigrateAsync( );
            }

            // Seed system holiday calendars
            await HolidayCalendarSeeder.SeedAsync( app.Services );

            // Seed task versions for pre-versioning tasks
            await TaskVersionSeeder.SeedAsync( app.Services );

            // Seed workflow versions for pre-versioning workflows
            await WorkflowVersionSeeder.SeedAsync( app.Services );

            // Seed trigger versions for pre-versioning triggers
            await TriggerVersionSeeder.SeedAsync( app.Services );

            // Seed configuration entries (migrates legacy ConfigurationSettings)
            await ConfigurationSeeder.SeedAsync( app.Services );

            // Seed retention policies
            await RetentionPolicySeeder.SeedAsync( app.Services );

            // Seed notification templates
            await NotificationTemplateSeeder.SeedAsync( app.Services );

            // Migrate per-agent path allowlists to ConfigurationEntry
            await PathAllowlistMigrationSeeder.SeedAsync( app.Services );

            // Configure the HTTP request pipeline.
            _ = app.UseExceptionHandler( );

            _ = app.MapOpenApi( );

            // Authentication & Authorization middleware
            _ = app.UseAuthentication( );
            _ = app.UseAuthorization( );

            // gRPC services
            _ = app.MapGrpcService<RegistrationGrpcService>( );
            _ = app.MapGrpcService<ScheduleSyncGrpcService>( );
            _ = app.MapGrpcService<JobReportingGrpcService>( );
            _ = app.MapGrpcService<OutputStreamingGrpcService>( );
            _ = app.MapGrpcService<VariableGrpcService>( );
            _ = app.MapGrpcService<TriggerEventGrpcService>( );
            _ = app.MapGrpcService<AuditEventGrpcService>( );
            _ = app.MapGrpcService<ConfigurationSyncGrpcService>( );
            _ = app.MapGrpcService<KeyExchangeGrpcService>( );
            _ = app.MapGrpcService<AgentHeartbeatGrpcService>( );

            // REST endpoints
            _ = app.MapStatusEndpoints( );
            _ = app.MapAuthProxyEndpoints( );
            _ = app.MapRegistrationEndpoints( );
            _ = app.MapAgentEndpoints( );
            _ = app.MapDiagnosticsEndpoints( );
            _ = app.MapScheduleEndpoints( );
            _ = app.MapTaskEndpoints( );
            _ = app.MapTaskVersionEndpoints( );
            _ = app.MapWorkflowVersionEndpoints( );
            _ = app.MapJobEndpoints( );
            _ = app.MapSettingsEndpoints( );
            _ = app.MapWorkflowEndpoints( );
            _ = app.MapVariableEndpoints( );
            _ = app.MapHolidayCalendarEndpoints( );
            _ = app.MapAuditEndpoints( );
            _ = app.MapEventEndpoints( );
            _ = app.MapShellEndpoints( );
            _ = app.MapFilterEndpoints( );
            _ = app.MapTriggerEndpoints( );
            _ = app.MapTriggerVersionEndpoints( );
            _ = app.MapCredentialEndpoints( );
            _ = app.MapRetentionEndpoints( );
            _ = app.MapNotificationEndpoints( );
            _ = app.MapUserPreferenceEndpoints( );

            _ = app.MapDefaultEndpoints( );

            app.Run( );
        } catch (Exception ex) {
            Log.Fatal( ex, "Werkr API Service terminated unexpectedly." );
        } finally {
            Log.CloseAndFlush( );
        }
    }
}
