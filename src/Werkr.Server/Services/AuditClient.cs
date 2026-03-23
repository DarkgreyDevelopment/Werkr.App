using Werkr.Common.Models.Audit;
using Werkr.Server.Identity;

namespace Werkr.Server.Services;

/// <summary>
/// Scoped service that sends audit events to the API via HTTP POST.
/// Used by Server components that need to record audit events before committing operations.
/// Sets <see cref="UserTokenContext"/> so the API receives the real user's identity.
/// </summary>
public sealed partial class AuditClient(
    IHttpClientFactory httpClientFactory,
    IUserTokenProvider userTokenProvider,
    ILogger<AuditClient> logger
) {
    /// <summary>
    /// Posts an audit event to the API. Returns true on success, false on failure.
    /// Logs errors but does not swallow exceptions for non-transient failures.
    /// </summary>
    public async Task<bool> LogAsync(
        AuditEventType eventType,
        string? actorId,
        string actorType,
        string? entityType,
        string? entityId,
        string action,
        object? details = null,
        CancellationToken ct = default
    ) {
        string eventTypeId = eventType.ToEventId( );
        try {
            string? userToken = await userTokenProvider.GetTokenAsync( );
            UserTokenContext.CurrentToken = userToken;

            using HttpClient client = httpClientFactory.CreateClient( "ApiService" );

            AuditEntry request = new(
                EventTypeId: eventTypeId,
                ActorId: actorId,
                ActorType: actorType,
                EntityType: entityType,
                EntityId: entityId,
                ActionPerformed: action,
                Details: details
            );

            HttpResponseMessage response = await client.PostAsJsonAsync( "/api/v1/audit", request, ct );

            if (response.IsSuccessStatusCode) {
                return true;
            }

            LogAuditPostFailed( logger, eventTypeId, (int)response.StatusCode );
            return false;
        } catch (Exception ex) {
            LogAuditPostException( logger, eventTypeId, ex );
            return false;
        }
    }

    [LoggerMessage( Level = LogLevel.Error,
        Message = "Audit POST failed for event '{EventTypeId}': HTTP {StatusCode}" )]
    private static partial void LogAuditPostFailed( ILogger logger, string eventTypeId, int statusCode );

    [LoggerMessage( Level = LogLevel.Error,
        Message = "Audit POST exception for event '{EventTypeId}'" )]
    private static partial void LogAuditPostException( ILogger logger, string eventTypeId, Exception ex );
}
