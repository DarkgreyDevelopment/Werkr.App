using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Werkr.Data.Entities.Notifications;

namespace Werkr.Data.Seeding;

/// <summary>
/// Seeds default notification templates for all (event type x channel type) combinations.
/// Idempotent — upserts missing templates on upgrade.
/// </summary>
public static class NotificationTemplateSeeder {

    /// <summary>
    /// Seeds default notification templates if none exist yet.
    /// </summary>
    public static async Task SeedAsync( IServiceProvider services ) {
        using IServiceScope scope = services.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        ILogger logger = services.GetRequiredService<ILoggerFactory>( )
            .CreateLogger( "Werkr.Data.Seeding.NotificationTemplateSeeder" );

        bool anyExist = await db.NotificationTemplates.AnyAsync( );
        if (anyExist) {
            int upserted = await UpsertMissingTemplatesAsync( db );
            if (upserted > 0) {
                LogUpserted( logger, upserted );
            }
            return;
        }

        DateTime now = DateTime.UtcNow;
        NotificationTemplate[] defaults = BuildAllDefaults( now );

        db.NotificationTemplates.AddRange( defaults );
        _ = await db.SaveChangesAsync( );

        LogSeeded( logger, defaults.Length );
    }

    private static async Task<int> UpsertMissingTemplatesAsync( WerkrDbContext db ) {
        HashSet<string> existing = [.. await db.NotificationTemplates
            .Select( t => $"{t.EventTypeId}:{t.ChannelType}" )
            .ToListAsync( )];

        DateTime now = DateTime.UtcNow;
        NotificationTemplate[] allDefaults = BuildAllDefaults( now );
        NotificationTemplate[] missing = [.. allDefaults.Where( t => !existing.Contains( $"{t.EventTypeId}:{t.ChannelType}" ) )];

        if (missing.Length > 0) {
            db.NotificationTemplates.AddRange( missing );
            _ = await db.SaveChangesAsync( );
        }

        return missing.Length;
    }

    private static NotificationTemplate[] BuildAllDefaults( DateTime now ) {
        List<NotificationTemplate> templates = [];

        // ── Workflow Execution ──
        AddTemplateSet( templates, now, "workflow.run.started",
            emailSubject: "[Werkr] Workflow \"{{workflowName}}\" started — Run {{runId}}",
            emailBody: "<h2>Workflow Started</h2><p>Workflow <strong>{{workflowName}}</strong> has started.</p><p>Run ID: {{runId}}<br/>Trigger: {{triggerType}}<br/>Time: {{timestamp}}</p><p><a href=\"{{link}}\">View Run Details</a></p>",
            inAppTitle: "Workflow \"{{workflowName}}\" started",
            inAppBody: "Run {{runId}} started at {{timestamp}}" );

        AddTemplateSet( templates, now, "workflow.run.completed",
            emailSubject: "[Werkr] Workflow \"{{workflowName}}\" completed — Run {{runId}}",
            emailBody: "<h2>Workflow Completed</h2><p>Workflow <strong>{{workflowName}}</strong> completed successfully.</p><p>Run ID: {{runId}}<br/>Duration: {{duration}}<br/>Steps: {{stepCount}}<br/>Time: {{timestamp}}</p><p><a href=\"{{link}}\">View Run Details</a></p>",
            inAppTitle: "Workflow \"{{workflowName}}\" completed",
            inAppBody: "Run {{runId}} completed in {{duration}}" );

        AddTemplateSet( templates, now, "workflow.run.failed",
            emailSubject: "[Werkr] Workflow \"{{workflowName}}\" failed — Run {{runId}}",
            emailBody: "<h2>Workflow Failed</h2><p>Workflow <strong>{{workflowName}}</strong> has failed.</p><p>Run ID: {{runId}}<br/>Failed Step: {{stepName}}<br/>Error: {{errorSummary}}<br/>Time: {{timestamp}}</p><p><a href=\"{{link}}\">View Run Details</a></p>",
            inAppTitle: "Workflow \"{{workflowName}}\" failed",
            inAppBody: "Step \"{{stepName}}\" failed: {{errorSummary}}" );

        // ── Approval ──
        AddTemplateSet( templates, now, "approval.requested",
            emailSubject: "[Werkr] Approval requested — {{workflowName}} / {{stepName}}",
            emailBody: "<h2>Approval Requested</h2><p>An approval is required for workflow <strong>{{workflowName}}</strong>.</p><p>Step: {{stepName}}<br/>Requested by: {{requesterName}}<br/>Time: {{timestamp}}</p><p><a href=\"{{link}}\">Review and Approve</a></p>",
            inAppTitle: "Approval requested: {{workflowName}}",
            inAppBody: "Step \"{{stepName}}\" requires approval from {{requesterName}}" );

        AddTemplateSet( templates, now, "approval.approved",
            emailSubject: "[Werkr] Approved — {{workflowName}} / {{stepName}}",
            emailBody: "<h2>Approval Granted</h2><p>Step <strong>{{stepName}}</strong> in workflow <strong>{{workflowName}}</strong> was approved.</p><p>Approved by: {{approverName}}<br/>Comment: {{comment}}<br/>Time: {{timestamp}}</p><p><a href=\"{{link}}\">View Details</a></p>",
            inAppTitle: "Approved: {{workflowName}} / {{stepName}}",
            inAppBody: "Approved by {{approverName}}: {{comment}}" );

        AddTemplateSet( templates, now, "approval.rejected",
            emailSubject: "[Werkr] Rejected — {{workflowName}} / {{stepName}}",
            emailBody: "<h2>Approval Rejected</h2><p>Step <strong>{{stepName}}</strong> in workflow <strong>{{workflowName}}</strong> was rejected.</p><p>Rejected by: {{approverName}}<br/>Comment: {{comment}}<br/>Time: {{timestamp}}</p><p><a href=\"{{link}}\">View Details</a></p>",
            inAppTitle: "Rejected: {{workflowName}} / {{stepName}}",
            inAppBody: "Rejected by {{approverName}}: {{comment}}" );

        AddTemplateSet( templates, now, "approval.timed_out",
            emailSubject: "[Werkr] Approval timed out — {{workflowName}} / {{stepName}}",
            emailBody: "<h2>Approval Timed Out</h2><p>The approval for step <strong>{{stepName}}</strong> in workflow <strong>{{workflowName}}</strong> timed out.</p><p>Auto-action: {{autoAction}}<br/>Time: {{timestamp}}</p><p><a href=\"{{link}}\">View Details</a></p>",
            inAppTitle: "Approval timed out: {{workflowName}}",
            inAppBody: "Step \"{{stepName}}\" timed out — auto-action: {{autoAction}}" );

        // ── Schedule ──
        AddTemplateSet( templates, now, "schedule.trigger.fired",
            emailSubject: "[Werkr] Trigger fired — {{triggerName}}",
            emailBody: "<h2>Schedule Trigger Fired</h2><p>Trigger <strong>{{triggerName}}</strong> fired for workflow <strong>{{workflowName}}</strong>.</p><p>Type: {{triggerType}}<br/>Time: {{timestamp}}</p>",
            inAppTitle: "Trigger fired: {{triggerName}}",
            inAppBody: "Workflow \"{{workflowName}}\" triggered by {{triggerName}}" );

        AddTemplateSet( templates, now, "schedule.trigger.suppressed",
            emailSubject: "[Werkr] Trigger suppressed — {{triggerName}} (holiday)",
            emailBody: "<h2>Schedule Trigger Suppressed</h2><p>Trigger <strong>{{triggerName}}</strong> was suppressed due to holiday.</p><p>Calendar: {{calendarName}}<br/>Holiday: {{holidayName}}<br/>Time: {{timestamp}}</p>",
            inAppTitle: "Trigger suppressed: {{triggerName}}",
            inAppBody: "Suppressed by holiday \"{{holidayName}}\" in {{calendarName}}" );

        // ── Security ──
        AddTemplateSet( templates, now, "security.auth.failure",
            emailSubject: "[Werkr] Authentication failure — {{userName}}",
            emailBody: "<h2>Authentication Failure</h2><p>A failed authentication attempt was detected.</p><p>User: {{userName}}<br/>IP: {{ipAddress}}<br/>Reason: {{reason}}<br/>Time: {{timestamp}}</p>",
            inAppTitle: "Authentication failure: {{userName}}",
            inAppBody: "Failed login from {{ipAddress}}: {{reason}}" );

        AddTemplateSet( templates, now, "security.authz.failure",
            emailSubject: "[Werkr] Authorization failure — {{userName}}",
            emailBody: "<h2>Authorization Failure</h2><p>An unauthorized access attempt was detected.</p><p>User: {{userName}}<br/>Resource: {{resource}}<br/>Action: {{action}}<br/>Time: {{timestamp}}</p>",
            inAppTitle: "Authorization failure: {{userName}}",
            inAppBody: "Unauthorized access to {{resource}} ({{action}})" );

        AddTemplateSet( templates, now, "security.key.rotated",
            emailSubject: "[Werkr] Key rotated — Agent {{agentName}}",
            emailBody: "<h2>Key Rotated</h2><p>The cryptographic key for agent <strong>{{agentName}}</strong> has been rotated.</p><p>Agent ID: {{agentId}}<br/>Time: {{timestamp}}</p>",
            inAppTitle: "Key rotated: {{agentName}}",
            inAppBody: "Agent {{agentName}} ({{agentId}}) key rotated" );

        // ── System ──
        AddTemplateSet( templates, now, "system.agent.online",
            emailSubject: "[Werkr] Agent online — {{agentName}}",
            emailBody: "<h2>Agent Online</h2><p>Agent <strong>{{agentName}}</strong> is now online.</p><p>Agent ID: {{agentId}}<br/>Version: {{agentVersion}}<br/>Time: {{timestamp}}</p>",
            inAppTitle: "Agent online: {{agentName}}",
            inAppBody: "Agent {{agentName}} (v{{agentVersion}}) connected" );

        AddTemplateSet( templates, now, "system.agent.offline",
            emailSubject: "[Werkr] Agent offline — {{agentName}}",
            emailBody: "<h2>Agent Offline</h2><p>Agent <strong>{{agentName}}</strong> has gone offline.</p><p>Agent ID: {{agentId}}<br/>Last seen: {{lastSeen}}<br/>Time: {{timestamp}}</p>",
            inAppTitle: "Agent offline: {{agentName}}",
            inAppBody: "Agent {{agentName}} offline — last seen {{lastSeen}}" );

        AddTemplateSet( templates, now, "system.config.changed",
            emailSubject: "[Werkr] Configuration changed — {{settingKey}}",
            emailBody: "<h2>Configuration Changed</h2><p>A platform configuration setting was updated.</p><p>Setting: {{settingKey}}<br/>Changed by: {{changedBy}}<br/>Time: {{timestamp}}</p>",
            inAppTitle: "Configuration changed: {{settingKey}}",
            inAppBody: "Setting \"{{settingKey}}\" updated by {{changedBy}}" );

        return [.. templates];
    }

    /// <summary>
    /// Adds email, webhook, and in-app templates for a single event type.
    /// </summary>
    private static void AddTemplateSet(
        List<NotificationTemplate> templates,
        DateTime now,
        string eventTypeId,
        string emailSubject,
        string emailBody,
        string inAppTitle,
        string inAppBody
    ) {
        // Email
        templates.Add( new NotificationTemplate {
            EventTypeId = eventTypeId,
            ChannelType = "email",
            Subject = emailSubject,
            Body = emailBody,
            IsDefault = true,
            CreatedUtc = now,
            ModifiedUtc = now,
        } );

        // Webhook — JSON payload template (the actual payload is built dynamically, body stores metadata hint)
        templates.Add( new NotificationTemplate {
            EventTypeId = eventTypeId,
            ChannelType = "webhook",
            Subject = null,
            Body = $"{{\"eventType\":\"{eventTypeId}\",\"data\":{{}}}}",
            IsDefault = true,
            CreatedUtc = now,
            ModifiedUtc = now,
        } );

        // In-App
        templates.Add( new NotificationTemplate {
            EventTypeId = eventTypeId,
            ChannelType = "inapp",
            Subject = inAppTitle,
            Body = inAppBody,
            IsDefault = true,
            CreatedUtc = now,
            ModifiedUtc = now,
        } );
    }

    private static readonly Action<ILogger, int, Exception?> s_logSeeded =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId( 1, "NotificationTemplatesSeeded" ),
            "Seeded {Count} notification templates." );

    private static void LogSeeded( ILogger logger, int count ) {
        s_logSeeded( logger, count, null );
    }

    private static readonly Action<ILogger, int, Exception?> s_logUpserted =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId( 2, "NotificationTemplatesUpserted" ),
            "Upserted {Count} missing notification templates on upgrade." );

    private static void LogUpserted( ILogger logger, int count ) {
        s_logUpserted( logger, count, null );
    }
}
