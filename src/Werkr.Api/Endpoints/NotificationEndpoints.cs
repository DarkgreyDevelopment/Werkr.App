using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Werkr.Common.Auth;
using Werkr.Common.Models.Audit;
using Werkr.Core.Audit;
using Werkr.Core.Notifications;
using Werkr.Data;
using Werkr.Data.Entities.Notifications;

namespace Werkr.Api.Endpoints;

/// <summary>Maps notification REST endpoints for channels, event types, templates, subscriptions, and user notifications.</summary>
internal static class NotificationEndpoints {

    /// <summary>Maps all notification endpoints.</summary>
    public static WebApplication MapNotificationEndpoints( this WebApplication app ) {

        // ══════════════════════════════════════════════════════════
        // ── Channel CRUD ──
        // ══════════════════════════════════════════════════════════

        // ── List all channels (config masked for secrets) ──
        _ = app.MapGet( "/api/v1/notifications/channels", async (
            WerkrDbContext db,
            CancellationToken ct
        ) => {
            List<NotificationChannelDto> channels = await db.NotificationChannels
                .AsNoTracking( )
                .OrderBy( c => c.Name )
                .Select( c => new NotificationChannelDto(
                    c.Id,
                    c.Name,
                    c.ChannelType,
                    "***",
                    c.IsEnabled,
                    c.CreatedUtc,
                    c.ModifiedUtc
                ) )
                .ToListAsync( ct );

            return Results.Ok( channels );
        } )
        .WithName( "ListNotificationChannels" )
        .RequireAuthorization( Policies.IsAdmin );

        // ── Create channel ──
        _ = app.MapPost( "/api/v1/notifications/channels", async (
            CreateNotificationChannelRequest request,
            HttpContext httpContext,
            WerkrDbContext db,
            IAuditService auditService,
            CancellationToken ct
        ) => {
            if (string.IsNullOrWhiteSpace( request.Name )) {
                return Results.BadRequest( new { message = "Name is required." } );
            }

            if (string.IsNullOrWhiteSpace( request.ChannelType )) {
                return Results.BadRequest( new { message = "ChannelType is required." } );
            }

            string[] validTypes = ["email", "webhook", "inapp"];
            if (!validTypes.Contains( request.ChannelType, StringComparer.OrdinalIgnoreCase )) {
                return Results.BadRequest( new { message = "ChannelType must be 'email', 'webhook', or 'inapp'." } );
            }

            bool nameExists = await db.NotificationChannels
                .AnyAsync( c => c.Name == request.Name, ct );

            if (nameExists) {
                return Results.Conflict( new { message = $"A channel named '{request.Name}' already exists." } );
            }

            string userId = httpContext.User.FindFirst( ClaimTypes.NameIdentifier )?.Value ?? "unknown";
            DateTime now = DateTime.UtcNow;

            NotificationChannel channel = new( ) {
                Name = request.Name,
                ChannelType = request.ChannelType.ToLowerInvariant( ),
                Configuration = request.ConfigurationJson ?? "{}",
                IsEnabled = request.IsEnabled ?? true,
                CreatedUtc = now,
                ModifiedUtc = now,
            };

            _ = db.NotificationChannels.Add( channel );
            _ = await db.SaveChangesAsync( ct );

            await auditService.LogAsync( new AuditEntry(
                EventTypeId: AuditEventType.NotificationChannelCreated.ToEventId( ),
                ActorId: userId,
                ActorType: "User",
                EntityType: "NotificationChannel",
                EntityId: channel.Id.ToString( ),
                ActionPerformed: "Created",
                Details: new { channel.Name, channel.ChannelType }
            ), ct );

            return Results.Created( $"/api/v1/notifications/channels/{channel.Id}", new NotificationChannelDto(
                channel.Id,
                channel.Name,
                channel.ChannelType,
                "***",
                channel.IsEnabled,
                channel.CreatedUtc,
                channel.ModifiedUtc
            ) );
        } )
        .WithName( "CreateNotificationChannel" )
        .RequireAuthorization( Policies.IsAdmin );

        // ── Update channel ──
        _ = app.MapPut( "/api/v1/notifications/channels/{id:long}", async (
            long id,
            UpdateNotificationChannelRequest request,
            HttpContext httpContext,
            WerkrDbContext db,
            IAuditService auditService,
            CancellationToken ct
        ) => {
            NotificationChannel? channel = await db.NotificationChannels
                .FirstOrDefaultAsync( c => c.Id == id, ct );

            if (channel is null) {
                return Results.NotFound( new { message = $"Channel {id} not found." } );
            }

            string userId = httpContext.User.FindFirst( ClaimTypes.NameIdentifier )?.Value ?? "unknown";

            if (!string.IsNullOrWhiteSpace( request.Name )) {
                bool nameExists = await db.NotificationChannels
                    .AnyAsync( c => c.Name == request.Name && c.Id != id, ct );
                if (nameExists) {
                    return Results.Conflict( new { message = $"A channel named '{request.Name}' already exists." } );
                }
                channel.Name = request.Name;
            }

            if (request.ConfigurationJson is not null) {
                channel.Configuration = request.ConfigurationJson;
            }

            if (request.IsEnabled.HasValue) {
                channel.IsEnabled = request.IsEnabled.Value;
            }

            channel.ModifiedUtc = DateTime.UtcNow;
            _ = await db.SaveChangesAsync( ct );

            await auditService.LogAsync( new AuditEntry(
                EventTypeId: AuditEventType.NotificationChannelUpdated.ToEventId( ),
                ActorId: userId,
                ActorType: "User",
                EntityType: "NotificationChannel",
                EntityId: channel.Id.ToString( ),
                ActionPerformed: "Updated",
                Details: new { channel.Name, channel.ChannelType }
            ), ct );

            return Results.Ok( new NotificationChannelDto(
                channel.Id,
                channel.Name,
                channel.ChannelType,
                "***",
                channel.IsEnabled,
                channel.CreatedUtc,
                channel.ModifiedUtc
            ) );
        } )
        .WithName( "UpdateNotificationChannel" )
        .RequireAuthorization( Policies.IsAdmin );

        // ── Delete channel ──
        _ = app.MapDelete( "/api/v1/notifications/channels/{id:long}", async (
            long id,
            HttpContext httpContext,
            WerkrDbContext db,
            IAuditService auditService,
            CancellationToken ct
        ) => {
            NotificationChannel? channel = await db.NotificationChannels
                .FirstOrDefaultAsync( c => c.Id == id, ct );

            if (channel is null) {
                return Results.NotFound( new { message = $"Channel {id} not found." } );
            }

            string userId = httpContext.User.FindFirst( ClaimTypes.NameIdentifier )?.Value ?? "unknown";
            string channelName = channel.Name;

            _ = db.NotificationChannels.Remove( channel );
            _ = await db.SaveChangesAsync( ct );

            await auditService.LogAsync( new AuditEntry(
                EventTypeId: AuditEventType.NotificationChannelDeleted.ToEventId( ),
                ActorId: userId,
                ActorType: "User",
                EntityType: "NotificationChannel",
                EntityId: id.ToString( ),
                ActionPerformed: "Deleted",
                Details: new { Name = channelName }
            ), ct );

            return Results.NoContent( );
        } )
        .WithName( "DeleteNotificationChannel" )
        .RequireAuthorization( Policies.IsAdmin );

        // ── Test notification ──
        _ = app.MapPost( "/api/v1/notifications/channels/{id:long}/test", async (
            long id,
            HttpContext httpContext,
            WerkrDbContext db,
            IEnumerable<INotificationChannel> channels,
            IAuditService auditService,
            CancellationToken ct
        ) => {
            NotificationChannel? channelEntity = await db.NotificationChannels
                .FirstOrDefaultAsync( c => c.Id == id, ct );

            if (channelEntity is null) {
                return Results.NotFound( new { message = $"Channel {id} not found." } );
            }

            if (!channelEntity.IsEnabled) {
                return Results.BadRequest( new { message = "Channel is disabled." } );
            }

            INotificationChannel? channelImpl = channels.FirstOrDefault( c =>
                string.Equals( c.ChannelType, channelEntity.ChannelType, StringComparison.OrdinalIgnoreCase ) );

            if (channelImpl is null) {
                return Results.BadRequest( new { message = $"No implementation registered for channel type '{channelEntity.ChannelType}'." } );
            }

            string userId = httpContext.User.FindFirst( ClaimTypes.NameIdentifier )?.Value ?? "unknown";

            NotificationDeliveryRequest testRequest = new(
                RecipientId: userId,
                EventTypeId: "system.config.changed",
                EventCategoryId: "system",
                TemplateName: "test",
                TemplateVariables: new Dictionary<string, string> {
                    ["settingKey"] = "notification.test",
                    ["changedBy"] = userId,
                    ["timestamp"] = DateTime.UtcNow.ToString( "O" ),
                },
                ChannelConfig: new NotificationChannelConfig(
                    ChannelId: channelEntity.Id,
                    ChannelType: channelEntity.ChannelType,
                    ConfigurationJson: channelEntity.Configuration
                )
            );

            DeliveryResult result = await channelImpl.DeliverAsync( testRequest, ct );

            await auditService.LogAsync( new AuditEntry(
                EventTypeId: AuditEventType.NotificationChannelTested.ToEventId( ),
                ActorId: userId,
                ActorType: "User",
                EntityType: "NotificationChannel",
                EntityId: channelEntity.Id.ToString( ),
                ActionPerformed: "Tested",
                Details: new { channelEntity.Name, channelEntity.ChannelType, result.Success, result.ErrorMessage }
            ), ct );

            return result.Success
                ? Results.Ok( new { message = "Test notification sent successfully." } )
                : Results.UnprocessableEntity( new { message = "Test delivery failed.", error = result.ErrorMessage } );
        } )
        .WithName( "TestNotificationChannel" )
        .RequireAuthorization( Policies.IsAdmin );

        // ══════════════════════════════════════════════════════════
        // ── Event Categories / Types ──
        // ══════════════════════════════════════════════════════════

        // ── List registered event categories and types ──
        _ = app.MapGet( "/api/v1/notifications/event-types", (
            INotificationEventCategoryRegistry registry
        ) => {
            IReadOnlyList<NotificationEventCategory> categories = registry.GetAll( );
            return Results.Ok( categories );
        } )
        .WithName( "ListNotificationEventTypes" )
        .RequireAuthorization( Policies.CanRead );

        // ══════════════════════════════════════════════════════════
        // ── Templates ──
        // ══════════════════════════════════════════════════════════

        // ── List templates ──
        _ = app.MapGet( "/api/v1/notifications/templates", async (
            string? eventTypeId,
            string? channelType,
            WerkrDbContext db,
            CancellationToken ct
        ) => {
            IQueryable<NotificationTemplate> query = db.NotificationTemplates.AsNoTracking( );

            if (!string.IsNullOrEmpty( eventTypeId )) {
                query = query.Where( t => t.EventTypeId == eventTypeId );
            }

            if (!string.IsNullOrEmpty( channelType )) {
                query = query.Where( t => t.ChannelType == channelType );
            }

            List<NotificationTemplate> templates = await query
                .OrderBy( t => t.EventTypeId ).ThenBy( t => t.ChannelType )
                .ToListAsync( ct );

            return Results.Ok( templates );
        } )
        .WithName( "ListNotificationTemplates" )
        .RequireAuthorization( Policies.IsAdmin );

        // ── Update template ──
        _ = app.MapPut( "/api/v1/notifications/templates/{id:long}", async (
            long id,
            UpdateNotificationTemplateRequest request,
            WerkrDbContext db,
            CancellationToken ct
        ) => {
            NotificationTemplate? template = await db.NotificationTemplates
                .FirstOrDefaultAsync( t => t.Id == id, ct );

            if (template is null) {
                return Results.NotFound( new { message = $"Template {id} not found." } );
            }

            if (request.Subject is not null) {
                template.Subject = request.Subject;
            }

            if (request.Body is not null) {
                template.Body = request.Body;
            }

            template.IsDefault = false;
            template.ModifiedUtc = DateTime.UtcNow;
            _ = await db.SaveChangesAsync( ct );

            return Results.Ok( template );
        } )
        .WithName( "UpdateNotificationTemplate" )
        .RequireAuthorization( Policies.IsAdmin );

        // ── Reset template to default ──
        _ = app.MapPost( "/api/v1/notifications/templates/{id:long}/reset", async (
            long id,
            WerkrDbContext db,
            CancellationToken ct
        ) => {
            NotificationTemplate? template = await db.NotificationTemplates
                .FirstOrDefaultAsync( t => t.Id == id, ct );

            if (template is null) {
                return Results.NotFound( new { message = $"Template {id} not found." } );
            }

            // Mark as default (actual content restoration happens via re-seeding)
            template.IsDefault = true;
            template.ModifiedUtc = DateTime.UtcNow;
            _ = await db.SaveChangesAsync( ct );

            return Results.Ok( template );
        } )
        .WithName( "ResetNotificationTemplate" )
        .RequireAuthorization( Policies.IsAdmin );

        // ══════════════════════════════════════════════════════════
        // ── Subscriptions ──
        // ══════════════════════════════════════════════════════════

        // ── List subscriptions ──
        _ = app.MapGet( "/api/v1/notifications/subscriptions", async (
            WerkrDbContext db,
            CancellationToken ct
        ) => {
            List<NotificationSubscription> subscriptions = await db.NotificationSubscriptions
                .AsNoTracking( )
                .Include( s => s.Channel )
                .OrderBy( s => s.CreatedUtc )
                .ToListAsync( ct );

            return Results.Ok( subscriptions.Select( s => new {
                s.Id,
                s.SubscriptionType,
                s.WorkflowId,
                s.Tag,
                s.EventCategoryId,
                s.ChannelId,
                ChannelName = s.Channel?.Name,
                s.IsEnabled,
                s.CreatedByUserId,
                s.CreatedUtc,
            } ) );
        } )
        .WithName( "ListNotificationSubscriptions" )
        .RequireAuthorization( Policies.IsAdmin );

        // ── Create subscription ──
        _ = app.MapPost( "/api/v1/notifications/subscriptions", async (
            CreateNotificationSubscriptionRequest request,
            HttpContext httpContext,
            WerkrDbContext db,
            IAuditService auditService,
            CancellationToken ct
        ) => {
            if (string.IsNullOrWhiteSpace( request.EventCategoryId )) {
                return Results.BadRequest( new { message = "EventCategoryId is required." } );
            }

            string userId = httpContext.User.FindFirst( System.Security.Claims.ClaimTypes.NameIdentifier )?.Value ?? "unknown";

            NotificationSubscription subscription = new( ) {
                SubscriptionType = request.SubscriptionType,
                WorkflowId = request.WorkflowId,
                Tag = request.Tag,
                EventCategoryId = request.EventCategoryId,
                ChannelId = request.ChannelId,
                IsEnabled = true,
                CreatedByUserId = userId,
                CreatedUtc = DateTime.UtcNow,
            };

            _ = db.NotificationSubscriptions.Add( subscription );
            _ = await db.SaveChangesAsync( ct );

            await auditService.LogAsync( new AuditEntry(
                EventTypeId: AuditEventType.NotificationSubscriptionCreated.ToEventId( ),
                ActorId: userId,
                ActorType: "User",
                EntityType: "NotificationSubscription",
                EntityId: subscription.Id.ToString( ),
                ActionPerformed: "Created",
                Details: new { subscription.SubscriptionType, subscription.EventCategoryId }
            ), ct );

            return Results.Created( $"/api/v1/notifications/subscriptions/{subscription.Id}", subscription );
        } )
        .WithName( "CreateNotificationSubscription" )
        .RequireAuthorization( Policies.IsAdmin );

        // ── Delete subscription ──
        _ = app.MapDelete( "/api/v1/notifications/subscriptions/{id:long}", async (
            long id,
            HttpContext httpContext,
            WerkrDbContext db,
            IAuditService auditService,
            CancellationToken ct
        ) => {
            NotificationSubscription? subscription = await db.NotificationSubscriptions
                .FirstOrDefaultAsync( s => s.Id == id, ct );

            if (subscription is null) {
                return Results.NotFound( new { message = $"Subscription {id} not found." } );
            }

            string userId = httpContext.User.FindFirst( System.Security.Claims.ClaimTypes.NameIdentifier )?.Value ?? "unknown";

            _ = db.NotificationSubscriptions.Remove( subscription );
            _ = await db.SaveChangesAsync( ct );

            await auditService.LogAsync( new AuditEntry(
                EventTypeId: AuditEventType.NotificationSubscriptionDeleted.ToEventId( ),
                ActorId: userId,
                ActorType: "User",
                EntityType: "NotificationSubscription",
                EntityId: id.ToString( ),
                ActionPerformed: "Deleted",
                Details: new { subscription.EventCategoryId }
            ), ct );

            return Results.NoContent( );
        } )
        .WithName( "DeleteNotificationSubscription" )
        .RequireAuthorization( Policies.IsAdmin );

        // ══════════════════════════════════════════════════════════
        // ── User Preferences ──
        // ══════════════════════════════════════════════════════════

        // ── Get current user's preferences ──
        _ = app.MapGet( "/api/v1/notifications/preferences", async (
            HttpContext httpContext,
            WerkrDbContext db,
            CancellationToken ct
        ) => {
            string userId = httpContext.User.FindFirst( System.Security.Claims.ClaimTypes.NameIdentifier )?.Value ?? "unknown";

            List<UserNotificationPreference> prefs = await db.UserNotificationPreferences
                .AsNoTracking( )
                .Where( p => p.UserId == userId )
                .OrderBy( p => p.EventCategoryId )
                .ToListAsync( ct );

            return Results.Ok( prefs );
        } )
        .WithName( "GetNotificationPreferences" )
        .RequireAuthorization( Policies.CanRead );

        // ── Update preference ──
        _ = app.MapPut( "/api/v1/notifications/preferences/{eventCategoryId}", async (
            string eventCategoryId,
            UpdateNotificationPreferenceRequest request,
            HttpContext httpContext,
            WerkrDbContext db,
            IAuditService auditService,
            CancellationToken ct
        ) => {
            string userId = httpContext.User.FindFirst( System.Security.Claims.ClaimTypes.NameIdentifier )?.Value ?? "unknown";

            UserNotificationPreference? pref = await db.UserNotificationPreferences
                .FirstOrDefaultAsync( p => p.UserId == userId && p.EventCategoryId == eventCategoryId, ct );

            if (pref is null) {
                pref = new UserNotificationPreference {
                    UserId = userId,
                    EventCategoryId = eventCategoryId,
                    ChannelType = request.ChannelType ?? "inapp",
                    IsEnabled = request.IsEnabled ?? true,
                    QuietHoursStart = request.QuietHoursStart,
                    QuietHoursEnd = request.QuietHoursEnd,
                    QuietHoursTimezone = request.QuietHoursTimezone,
                };
                _ = db.UserNotificationPreferences.Add( pref );
            } else {
                if (request.ChannelType is not null) {
                    pref.ChannelType = request.ChannelType;
                }
                if (request.IsEnabled.HasValue) {
                    pref.IsEnabled = request.IsEnabled.Value;
                }
                pref.QuietHoursStart = request.QuietHoursStart ?? pref.QuietHoursStart;
                pref.QuietHoursEnd = request.QuietHoursEnd ?? pref.QuietHoursEnd;
                pref.QuietHoursTimezone = request.QuietHoursTimezone ?? pref.QuietHoursTimezone;
            }

            _ = await db.SaveChangesAsync( ct );

            await auditService.LogAsync( new AuditEntry(
                EventTypeId: AuditEventType.NotificationPreferenceUpdated.ToEventId( ),
                ActorId: userId,
                ActorType: "User",
                EntityType: "UserNotificationPreference",
                EntityId: $"{userId}:{eventCategoryId}",
                ActionPerformed: "Updated",
                Details: new { eventCategoryId, pref.ChannelType, pref.IsEnabled }
            ), ct );

            return Results.Ok( pref );
        } )
        .WithName( "UpdateNotificationPreference" )
        .RequireAuthorization( Policies.CanRead );

        // ══════════════════════════════════════════════════════════
        // ── User Notifications (in-app) ──
        // ══════════════════════════════════════════════════════════

        // ── List current user's notifications ──
        _ = app.MapGet( "/api/v1/notifications", async (
            bool? isRead,
            int? page,
            int? pageSize,
            HttpContext httpContext,
            WerkrDbContext db,
            CancellationToken ct
        ) => {
            string userId = httpContext.User.FindFirst( System.Security.Claims.ClaimTypes.NameIdentifier )?.Value ?? "unknown";
            int p = Math.Max( 1, page ?? 1 );
            int ps = Math.Clamp( pageSize ?? 20, 1, 100 );

            IQueryable<UserNotification> query = db.UserNotifications
                .AsNoTracking( )
                .Where( n => n.UserId == userId );

            if (isRead.HasValue) {
                query = query.Where( n => n.IsRead == isRead.Value );
            }

            List<UserNotification> notifications = await query
                .OrderByDescending( n => n.CreatedUtc )
                .Skip( (p - 1) * ps )
                .Take( ps )
                .ToListAsync( ct );

            return Results.Ok( notifications );
        } )
        .WithName( "ListUserNotifications" )
        .RequireAuthorization( Policies.CanRead );

        // ── Mark notification as read ──
        _ = app.MapPut( "/api/v1/notifications/{id:long}/read", async (
            long id,
            HttpContext httpContext,
            WerkrDbContext db,
            CancellationToken ct
        ) => {
            string userId = httpContext.User.FindFirst( System.Security.Claims.ClaimTypes.NameIdentifier )?.Value ?? "unknown";

            UserNotification? notification = await db.UserNotifications
                .FirstOrDefaultAsync( n => n.Id == id && n.UserId == userId, ct );

            if (notification is null) {
                return Results.NotFound( );
            }

            notification.IsRead = true;
            notification.ReadUtc = DateTime.UtcNow;
            _ = await db.SaveChangesAsync( ct );

            return Results.Ok( notification );
        } )
        .WithName( "MarkNotificationRead" )
        .RequireAuthorization( Policies.CanRead );

        // ── Mark all as read ──
        _ = app.MapPut( "/api/v1/notifications/read-all", async (
            HttpContext httpContext,
            WerkrDbContext db,
            CancellationToken ct
        ) => {
            string userId = httpContext.User.FindFirst( System.Security.Claims.ClaimTypes.NameIdentifier )?.Value ?? "unknown";
            DateTime now = DateTime.UtcNow;

            int updated = await db.UserNotifications
                .Where( n => n.UserId == userId && !n.IsRead )
                .ExecuteUpdateAsync( s => s
                    .SetProperty( n => n.IsRead, true )
                    .SetProperty( n => n.ReadUtc, now ), ct );

            return Results.Ok( new { updated } );
        } )
        .WithName( "MarkAllNotificationsRead" )
        .RequireAuthorization( Policies.CanRead );

        // ── Unread count ──
        _ = app.MapGet( "/api/v1/notifications/unread-count", async (
            HttpContext httpContext,
            WerkrDbContext db,
            CancellationToken ct
        ) => {
            string userId = httpContext.User.FindFirst( System.Security.Claims.ClaimTypes.NameIdentifier )?.Value ?? "unknown";

            int count = await db.UserNotifications
                .CountAsync( n => n.UserId == userId && !n.IsRead, ct );

            return Results.Ok( new { count } );
        } )
        .WithName( "GetUnreadNotificationCount" )
        .RequireAuthorization( Policies.CanRead );

        // ══════════════════════════════════════════════════════════
        // ── Delivery Tracking (admin) ──
        // ══════════════════════════════════════════════════════════

        // ── Dead-lettered deliveries ──
        _ = app.MapGet( "/api/v1/notifications/dead-letter", async (
            WerkrDbContext db,
            CancellationToken ct
        ) => {
            List<NotificationDelivery> deadLettered = await db.NotificationDeliveries
                .AsNoTracking( )
                .Where( d => d.Status == DeliveryStatus.DeadLettered )
                .OrderByDescending( d => d.CompletedUtc )
                .Take( 100 )
                .ToListAsync( ct );

            return Results.Ok( deadLettered );
        } )
        .WithName( "ListDeadLetteredDeliveries" )
        .RequireAuthorization( Policies.IsAdmin );

        // ── Delivery history ──
        _ = app.MapGet( "/api/v1/notifications/deliveries", async (
            int? page,
            int? pageSize,
            WerkrDbContext db,
            CancellationToken ct
        ) => {
            int p = Math.Max( 1, page ?? 1 );
            int ps = Math.Clamp( pageSize ?? 20, 1, 100 );

            List<NotificationDelivery> deliveries = await db.NotificationDeliveries
                .AsNoTracking( )
                .OrderByDescending( d => d.CreatedUtc )
                .Skip( (p - 1) * ps )
                .Take( ps )
                .ToListAsync( ct );

            return Results.Ok( deliveries );
        } )
        .WithName( "ListNotificationDeliveries" )
        .RequireAuthorization( Policies.IsAdmin );

        return app;
    }
}

// ── DTOs ──

/// <summary>Channel listing DTO (configuration masked).</summary>
internal record NotificationChannelDto(
    long Id,
    string Name,
    string ChannelType,
    string Configuration,
    bool IsEnabled,
    DateTime CreatedUtc,
    DateTime ModifiedUtc
);

/// <summary>Request body for creating a notification channel.</summary>
internal record CreateNotificationChannelRequest(
    string? Name,
    string? ChannelType,
    string? ConfigurationJson,
    bool? IsEnabled
);

/// <summary>Request body for updating a notification channel.</summary>
internal record UpdateNotificationChannelRequest(
    string? Name,
    string? ConfigurationJson,
    bool? IsEnabled
);

/// <summary>Request body for updating a notification template.</summary>
internal record UpdateNotificationTemplateRequest(
    string? Subject,
    string? Body
);

/// <summary>Request body for creating a notification subscription.</summary>
internal record CreateNotificationSubscriptionRequest(
    NotificationSubscriptionType SubscriptionType,
    long? WorkflowId,
    string? Tag,
    string? EventCategoryId,
    long ChannelId
);

/// <summary>Request body for updating user notification preferences.</summary>
internal record UpdateNotificationPreferenceRequest(
    string? ChannelType,
    bool? IsEnabled,
    TimeOnly? QuietHoursStart,
    TimeOnly? QuietHoursEnd,
    string? QuietHoursTimezone
);
