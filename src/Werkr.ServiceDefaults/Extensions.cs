using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;

namespace Werkr.ServiceDefaults;

// Adds common Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/dotnet/aspire/service-defaults

/// <summary>
/// Provides shared service default extensions for Aspire-based applications,
/// including OpenTelemetry, health checks, service discovery, and endpoint mapping.
/// </summary>
public static class Extensions {
    /// <summary>
    /// The URL path used for the comprehensive health check endpoint.
    /// </summary>
    private const string HealthEndpointPath = "/health";

    /// <summary>
    /// The URL path used for the liveness-only health check endpoint, which verifies the process is running.
    /// </summary>
    private const string AlivenessEndpointPath = "/alive";

    /// <summary>
    /// Adds common service defaults for the host builder.
    /// </summary>
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The builder to configure.</param>
    /// <returns>The configured builder.</returns>
    public static TBuilder AddServiceDefaults<TBuilder>( this TBuilder builder )
        where TBuilder : IHostApplicationBuilder {
        _ = builder.ConfigureOpenTelemetry( );

        _ = builder.AddDefaultHealthChecks( );

        _ = builder.Services.AddServiceDiscovery( );

        _ = builder.Services.ConfigureHttpClientDefaults( http => {
            // Turn on resilience by default
            _ = http.AddStandardResilienceHandler( );

            // Turn on service discovery by default
            _ = http.AddServiceDiscovery( );
        } );

        return builder;
    }

    /// <summary>
    /// Configures OpenTelemetry logging, metrics, and tracing for the host builder.
    /// </summary>
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The builder to configure.</param>
    /// <returns>The configured builder.</returns>
    public static TBuilder ConfigureOpenTelemetry<TBuilder>( this TBuilder builder )
        where TBuilder : IHostApplicationBuilder {
        _ = builder.Services.AddOpenTelemetry( )
            .WithLogging( )
            .WithMetrics( metrics => {
                _ = metrics.AddMeter( "Microsoft.AspNetCore.Hosting" )
                    .AddMeter( "Microsoft.AspNetCore.Server.Kestrel" )
                    .AddMeter( "System.Net.Http" )
                    .AddMeter( "System.Runtime" );
            } )
            .WithTracing( tracing => {
                _ = tracing.AddSource( builder.Environment.ApplicationName )
                    .AddSource( "Microsoft.AspNetCore" )
                    .AddSource( "System.Net.Http" );
            } );

        _ = builder.AddOpenTelemetryExporters( );

        return builder;
    }

    /// <summary>
    /// Conditionally adds the OpenTelemetry Protocol (OTLP) exporter
    /// when the <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> configuration value
    /// is present and non-empty.
    /// </summary>
    private static TBuilder AddOpenTelemetryExporters<TBuilder>( this TBuilder builder )
        where TBuilder : IHostApplicationBuilder {
        bool useOtlpExporter = !string.IsNullOrWhiteSpace( builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] );

        if (useOtlpExporter) {
            _ = builder.Services.AddOpenTelemetry( ).UseOtlpExporter( );
        }

        return builder;
    }

    /// <summary>
    /// Adds default health checks including a basic liveness probe.
    /// </summary>
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The builder to configure.</param>
    /// <returns>The configured builder.</returns>
    public static TBuilder AddDefaultHealthChecks<TBuilder>( this TBuilder builder )
        where TBuilder : IHostApplicationBuilder {
        _ = builder.Services.AddHealthChecks( )
            // Add a default liveness check to ensure app is responsive
            .AddCheck(
                "self",
                ( ) => HealthCheckResult.Healthy( ),
                ["live"]
            );

        return builder;
    }

    /// <summary>
    /// Maps default health endpoints for load balancer and orchestrator integration.
    /// </summary>
    /// <param name="app">The web application instance.</param>
    /// <returns>The application for further endpoint mapping.</returns>
    public static WebApplication MapDefaultEndpoints( this WebApplication app ) {
        // All health checks must pass for app to be considered ready to accept traffic after starting
        _ = app.MapHealthChecks( HealthEndpointPath );

        // Only health checks tagged with the "live" tag must pass for app to be considered alive
        _ = app.MapHealthChecks(
            AlivenessEndpointPath,
            new HealthCheckOptions {
                Predicate = r => r.Tags.Contains( "live" )
            }
        );

        return app;
    }
}
