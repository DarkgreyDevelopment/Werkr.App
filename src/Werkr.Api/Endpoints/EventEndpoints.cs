using System.Text.Json;
using Werkr.Common.Auth;
using Werkr.Core.Communication;

namespace Werkr.Api.Endpoints;

/// <summary>
/// Maps Server-Sent Events (SSE) endpoints for real-time job event streaming.
/// Clients (e.g. the Blazor Server) subscribe to <c>/api/events/jobs</c> to receive
/// live notifications when agents report job completions.
/// </summary>
internal static class EventEndpoints {

    private static readonly JsonSerializerOptions s_jsonOptions = new( ) {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Maps the <c>/api/events/jobs</c> SSE endpoint.</summary>
    public static WebApplication MapEventEndpoints( this WebApplication app ) {
        _ = app.MapGet( "/api/events/jobs", async (
            JobEventBroadcaster broadcaster,
            HttpContext httpContext,
            CancellationToken ct
        ) => {
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";

            using JobEventSubscription subscription = broadcaster.Subscribe( );

            try {
                await foreach (JobEvent jobEvent in subscription.Reader.ReadAllAsync( ct )) {
                    string json = JsonSerializer.Serialize( jobEvent, s_jsonOptions );
                    string eventType = jobEvent.WorkflowRunId.HasValue
                        ? "workflow-job"
                        : "task-job";

                    await httpContext.Response.WriteAsync( $"event: {eventType}\n", ct );
                    await httpContext.Response.WriteAsync( $"data: {json}\n\n", ct );
                    await httpContext.Response.Body.FlushAsync( ct );
                }
            } catch (OperationCanceledException) {
                // Client disconnected — normal SSE lifecycle.
            }
        } )
        .WithName( "StreamJobEvents" )
        .RequireAuthorization( Policies.CanRead )
        .ExcludeFromDescription( );

        return app;
    }
}
