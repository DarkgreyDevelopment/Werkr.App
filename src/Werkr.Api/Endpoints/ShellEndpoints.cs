using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Werkr.Api.Services;
using Werkr.Common.Auth;
using Werkr.Common.Models;
using Werkr.Common.Protos;
using Werkr.Core.Scheduling;
using Werkr.Data;
using Werkr.Data.Entities.Tasks;

namespace Werkr.Api.Endpoints;

/// <summary>
/// Maps the interactive shell streaming endpoint.  A browser client POSTs a
/// command to <c>/api/agents/{agentId}/shell/stream</c>; the server creates an
/// ephemeral task + one-time schedule, pushes a schedule invalidation to the
/// agent, subscribes to the output stream, and SSE-forwards each line to the
/// browser in real time.
/// </summary>
internal static class ShellEndpoints {

    private static readonly JsonSerializerOptions s_jsonOptions = new( ) {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Maps the shell streaming endpoint.</summary>
    public static WebApplication MapShellEndpoints( this WebApplication app ) {
        _ = app.MapPost( "/api/agents/{agentId}/shell/stream", async (
            Guid agentId,
            ExecuteCommandRequest request,
            RunNowService runNowService,
            ScheduleInvalidationDispatcher invalidationDispatcher,
            OutputStreamingGrpcService outputStreaming,
            WerkrDbContext dbContext,
            HttpContext httpContext,
            CancellationToken ct
        ) => {
            TaskActionType actionType = request.ActionType.HasValue
                ? (TaskActionType) request.ActionType.Value
                : TaskActionType.ShellCommand;

            // Look up the agent's tags so the ephemeral task routes to this agent
            string[]? agentTags = await dbContext.RegisteredConnections
                .AsNoTracking( )
                .Where( c => c.Id == agentId && !c.IsServer )
                .Select( c => c.Tags )
                .FirstOrDefaultAsync( ct );

            // Create ephemeral task + one-time schedule
            (long taskId, Guid scheduleId) = await runNowService.CreateEphemeralTaskAsync(
                request.Command, actionType, agentTags, ct );

            // Subscribe to the output stream BEFORE pushing invalidation so the
            // agent's subscription is registered before execution can begin.
            string scheduleIdStr = scheduleId.ToString( );
            Channel<OutputMessage>? channel =
                await outputStreaming.SubscribeAsync( taskId, scheduleIdStr );

            // Push invalidation so the agent picks it up immediately
            await invalidationDispatcher.InvalidateAsync( scheduleId, ct );

            if (channel is null) {
                // No agent stream available yet — return 202 with IDs so client can poll
                httpContext.Response.StatusCode = StatusCodes.Status202Accepted;
                await httpContext.Response.WriteAsJsonAsync( new {
                    taskId,
                    scheduleId,
                    message = "Ephemeral task created. No agent stream available for live output.",
                }, ct );
                return;
            }

            // Stream output as SSE
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";

            // Send initial metadata so client knows the task/schedule IDs
            string meta = JsonSerializer.Serialize( new { taskId, scheduleId }, s_jsonOptions );
            await httpContext.Response.WriteAsync( $"event: meta\ndata: {meta}\n\n", ct );
            await httpContext.Response.Body.FlushAsync( ct );

            try {
                await foreach (OutputMessage message in channel.Reader.ReadAllAsync( ct )) {
                    if (message.PayloadCase == OutputMessage.PayloadOneofCase.Line) {
                        string json = JsonSerializer.Serialize( new {
                            message = message.Line.Text,
                            logLevel = message.Line.LogLevel,
                            timestamp = message.Line.Timestamp,
                        }, s_jsonOptions );
                        await httpContext.Response.WriteAsync( $"event: output\ndata: {json}\n\n", ct );
                        await httpContext.Response.Body.FlushAsync( ct );
                    } else if (message.PayloadCase == OutputMessage.PayloadOneofCase.Complete) {
                        string json = JsonSerializer.Serialize( new {
                            exitCode = message.Complete.ExitCode,
                            success = message.Complete.Success,
                            errorMessage = message.Complete.ErrorMessage,
                        }, s_jsonOptions );
                        await httpContext.Response.WriteAsync( $"event: complete\ndata: {json}\n\n", ct );
                        await httpContext.Response.Body.FlushAsync( ct );
                        break; // Execution is done
                    }
                }
            } catch (OperationCanceledException) {
                // Client disconnected — normal SSE lifecycle.
            } finally {
                await outputStreaming.UnsubscribeAsync( taskId, scheduleIdStr );
            }
        } )
        .WithName( "StreamShellOutput" )
        .RequireAuthorization( Policies.CanExecute )
        .ExcludeFromDescription( );

        return app;
    }
}
