using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Werkr.Common.Auth;
using Werkr.Server.Services;

namespace Werkr.Server.Hubs;

/// <summary>
/// SignalR hub for real-time workflow run event push.
/// Clients join/leave run-specific groups to receive step status, run status, and log events.
/// </summary>
[Authorize]
public sealed class WorkflowRunHub : Hub {

    /// <summary>
    /// Subscribes the caller to events for the specified workflow run.
    /// Requires <see cref="Policies.CanRead"/> authorization.
    /// </summary>
    public async Task JoinRun( Guid runId ) {
        IAuthorizationService authService = Context.GetHttpContext( )!.RequestServices
            .GetRequiredService<IAuthorizationService>( );
        AuthorizationResult authResult = await authService.AuthorizeAsync(
            Context.User!, Policies.CanRead );

        if (!authResult.Succeeded) {
            throw new HubException( "Unauthorized" );
        }

        JobEventRelayService.TrackRun( runId );
        await Groups.AddToGroupAsync( Context.ConnectionId, runId.ToString( ) );
    }

    /// <summary>
    /// Unsubscribes the caller from events for the specified workflow run.
    /// </summary>
    public async Task LeaveRun( Guid runId ) {
        JobEventRelayService.UntrackRun( runId );
        await Groups.RemoveFromGroupAsync( Context.ConnectionId, runId.ToString( ) );
    }
}
