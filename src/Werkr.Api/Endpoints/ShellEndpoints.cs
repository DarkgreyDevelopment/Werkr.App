using System.Text.Json;
using Werkr.Common.Auth;
using Werkr.Common.Models;
using Werkr.Core.Communication;

namespace Werkr.Api.Endpoints;

/// <summary>Maps the SSE streaming shell-execution endpoint.</summary>
internal static class ShellEndpoints {
    /// <summary>
    /// Static <see cref="JsonSerializerOptions"/> configured with camelCase property naming for serializing SSE data payloads.
    /// </summary>
    private static readonly JsonSerializerOptions s_jsonOptions = new( ) {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Maps <c>POST /api/agents/{agentId}/shell/stream</c> - streams operator
    /// output as Server-Sent Events so the Blazor UI can render lines in real time.
    /// </summary>
    public static WebApplication MapShellEndpoints( this WebApplication app ) {
        _ = app.MapPost( "/api/agents/{agentId}/shell/stream", async (
            Guid agentId,
            ExecuteCommandRequest request,
            CommandDispatcher commandDispatcher,
            HttpContext httpContext,
            CancellationToken ct
        ) => {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource( ct );
            if (request.TimeoutMinutes > 0) {
                cts.CancelAfter( TimeSpan.FromMinutes( request.TimeoutMinutes ) );
            }

            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";
            httpContext.Response.Headers["X-Accel-Buffering"] = "no"; // disable nginx buffering

            OperatorType operatorType = Enum.Parse<OperatorType>( request.OperatorType, ignoreCase: true );

            try {
                await foreach (OperatorOutput output in commandDispatcher.ExecuteCommandAsync(
                    agentId, operatorType, request.Command, cts.Token )) {
                    OperatorOutputLine line = new( output.LogLevel, output.Message, output.Timestamp );
                    string json = JsonSerializer.Serialize( line, s_jsonOptions );

                    await httpContext.Response.WriteAsync( $"data: {json}\n\n", cts.Token );
                    await httpContext.Response.Body.FlushAsync( cts.Token );
                }

                // Signal completion
                await httpContext.Response.WriteAsync( "event: done\ndata: {}\n\n", cts.Token );
                await httpContext.Response.Body.FlushAsync( cts.Token );
            } catch (OperationCanceledException) {
                // Client disconnected or timeout — write nothing further
            } catch (CommandDispatcherException ex) {
                string errorJson = JsonSerializer.Serialize(
                        new { message = ex.UserMessage }, s_jsonOptions );
                await httpContext.Response.WriteAsync( $"event: error\ndata: {errorJson}\n\n", CancellationToken.None );
                await httpContext.Response.Body.FlushAsync( CancellationToken.None );
            }
        } )
        .WithName( "StreamShellExecute" )
        .RequireAuthorization( Policies.CanExecute );

        return app;
    }
}
