using System.Text.Json;
using Werkr.Common.Auth;
using Werkr.Core.Communication;

namespace Werkr.Api.Endpoints;

/// <summary>
/// Maps Server-Sent Events (SSE) endpoints for real-time job and workflow event streaming.
/// </summary>
internal static class EventEndpoints {

    private static readonly JsonSerializerOptions s_jsonOptions = new( ) {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Maps all SSE endpoints.</summary>
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

        _ = app.MapGet( "/api/events/workflow-runs", async (
            WorkflowEventBroadcaster broadcaster,
            HttpContext httpContext,
            CancellationToken ct
        ) => {
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";

            using WorkflowEventSubscription subscription = broadcaster.Subscribe( );

            try {
                await foreach (WorkflowEvent workflowEvent in subscription.Reader.ReadAllAsync( ct )) {
                    string eventType = GetEventType( workflowEvent );
                    string json = JsonSerializer.Serialize( workflowEvent, workflowEvent.GetType( ), s_jsonOptions );

                    await httpContext.Response.WriteAsync( $"event: {eventType}\n", ct );
                    await httpContext.Response.WriteAsync( $"data: {json}\n\n", ct );
                    await httpContext.Response.Body.FlushAsync( ct );
                }
            } catch (OperationCanceledException) {
                // Client disconnected — normal SSE lifecycle.
            }
        } )
        .WithName( "StreamWorkflowEvents" )
        .RequireAuthorization( Policies.CanRead )
        .ExcludeFromDescription( );

        _ = app.MapGet( "/api/workflows/runs/{runId:guid}/events", async (
            Guid runId,
            WorkflowEventBroadcaster broadcaster,
            HttpContext httpContext,
            CancellationToken ct
        ) => {
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";

            using WorkflowEventSubscription subscription = broadcaster.Subscribe( );

            try {
                await foreach (WorkflowEvent workflowEvent in subscription.Reader.ReadAllAsync( ct )) {
                    if (workflowEvent.WorkflowRunId != runId) {
                        continue;
                    }

                    string eventType = GetEventType( workflowEvent );
                    string json = JsonSerializer.Serialize( workflowEvent, workflowEvent.GetType( ), s_jsonOptions );

                    await httpContext.Response.WriteAsync( $"event: {eventType}\n", ct );
                    await httpContext.Response.WriteAsync( $"data: {json}\n\n", ct );
                    await httpContext.Response.Body.FlushAsync( ct );
                }
            } catch (OperationCanceledException) {
                // Client disconnected — normal SSE lifecycle.
            }
        } )
        .WithName( "StreamWorkflowRunEvents" )
        .RequireAuthorization( Policies.CanRead )
        .ExcludeFromDescription( );

        return app;
    }

    private static string GetEventType( WorkflowEvent workflowEvent ) => workflowEvent switch {
        StepStartedEvent => "step-started",
        StepCompletedEvent => "step-completed",
        StepFailedEvent => "step-failed",
        StepSkippedEvent => "step-skipped",
        RunCompletedEvent => "run-completed",
        LogAppendedEvent => "log-appended",
        _ => "unknown",
    };
}
