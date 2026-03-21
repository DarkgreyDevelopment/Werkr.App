using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Werkr.Common.Models.Audit;
using Werkr.Core.Audit;
using Werkr.Data;
using Werkr.Data.Entities.Audit;

namespace Werkr.Api.Services;

/// <summary>
/// Background service that runs periodically to delete <see cref="AuditEvent"/>
/// records older than the configured retention period, then logs a summary event.
/// </summary>
public sealed partial class AuditLogCleanupService(
    IServiceScopeFactory scopeFactory,
    IOptions<AuditLogOptions> options,
    ILogger<AuditLogCleanupService> logger
) : BackgroundService {
    /// <summary>
    /// The factory used to create scoped service providers for resolving database contexts.
    /// </summary>
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    /// <summary>
    /// Snapshot of the audit log cleanup configuration options captured at service creation time.
    /// </summary>
    private readonly AuditLogOptions _options = options.Value;
    /// <summary>
    /// Logger instance used by the source-generated log methods in this partial class.
    /// </summary>
    private readonly ILogger<AuditLogCleanupService> _logger = logger;

    /// <inheritdoc />
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        int effectiveInterval = Math.Max( _options.SweepIntervalMinutes, AuditLogOptions.MinSweepIntervalMinutes );

        if (_options.SweepIntervalMinutes < AuditLogOptions.MinSweepIntervalMinutes) {
            LogSweepIntervalClamped( _logger, _options.SweepIntervalMinutes, effectiveInterval );
        }

        if (_options.RetentionDays < 7) {
            LogLowRetentionDays( _logger, _options.RetentionDays );
        }

        LogCleanupStarted( _logger, _options.RetentionDays, effectiveInterval );

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await CleanupAsync( stoppingToken );
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                LogCleanupError( _logger, ex );
            }

            await Task.Delay( TimeSpan.FromMinutes( effectiveInterval ), stoppingToken );
        }
    }

    /// <summary>
    /// Performs a single audit log cleanup pass. Before deleting, queries for summary
    /// information (date range, category breakdown). After deleting, records a summary
    /// audit event with event type <c>audit.retention.cleanup</c>.
    /// </summary>
    private async Task CleanupAsync( CancellationToken ct ) {
        using IServiceScope scope = _scopeFactory.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        IAuditService auditService = scope.ServiceProvider.GetRequiredService<IAuditService>( );

        DateTime cutoff = DateTime.UtcNow.AddDays( -_options.RetentionDays );

        // Gather summary before deletion
        IQueryable<AuditEvent> expiredQuery = db.AuditEvents.Where( e => e.TimestampUtc < cutoff );
        int countToDelete = await expiredQuery.CountAsync( ct );

        if (countToDelete == 0) {
            return;
        }

        DateTime? earliest = await expiredQuery.MinAsync( e => (DateTime?) e.TimestampUtc, ct );
        DateTime? latest = await expiredQuery.MaxAsync( e => (DateTime?) e.TimestampUtc, ct );

        Dictionary<string, int> categoryCounts = await expiredQuery
            .GroupBy( e => e.EventCategory )
            .Select( g => new { Category = g.Key, Count = g.Count( ) } )
            .ToDictionaryAsync( x => x.Category, x => x.Count, ct );

        // Delete expired records
        int deleted = await expiredQuery.ExecuteDeleteAsync( ct );

        LogRecordsDeleted( _logger, deleted, _options.RetentionDays );

        // Record a summary audit event
        object summary = new {
            deletedCount = deleted,
            earliestTimestamp = earliest,
            latestTimestamp = latest,
            categoryCounts
        };

        bool auditRecorded = false;
        for (int attempt = 1; attempt <= 3 && !auditRecorded; attempt++) {
            try {
                await auditService.LogAsync( new AuditEntry(
                    EventTypeId: AuditEventType.AuditRetentionCleanup.ToEventId( ),
                    ActorId: null,
                    ActorType: "System",
                    EntityType: null,
                    EntityId: null,
                    ActionPerformed: "Cleanup",
                    Details: summary
                ), ct );
                auditRecorded = true;
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                if (attempt < 3) {
                    LogSummaryAuditRetry( _logger, attempt, ex );
                    await Task.Delay( TimeSpan.FromSeconds( attempt ), ct );
                } else {
                    LogSummaryAuditExhausted( _logger, deleted, earliest, latest, ex );
                }
            }
        }
    }

    [LoggerMessage( Level = LogLevel.Warning,
        Message = "Configured SweepIntervalMinutes ({Configured}) is below minimum. Clamped to {Effective} minutes." )]
    private static partial void LogSweepIntervalClamped( ILogger logger, int configured, int effective );

    [LoggerMessage( Level = LogLevel.Warning,
        Message = "Audit log retention is set to {RetentionDays} days, which is below the recommended minimum of 7 days" )]
    private static partial void LogLowRetentionDays( ILogger logger, int retentionDays );

    [LoggerMessage( Level = LogLevel.Information,
        Message = "Audit log cleanup service started (retention: {RetentionDays} days, sweep interval: {SweepIntervalMinutes} minutes)" )]
    private static partial void LogCleanupStarted( ILogger logger, int retentionDays, int sweepIntervalMinutes );

    [LoggerMessage( Level = LogLevel.Information,
        Message = "Audit log cleanup deleted {Count} records older than {RetentionDays} days" )]
    private static partial void LogRecordsDeleted( ILogger logger, int count, int retentionDays );

    [LoggerMessage( Level = LogLevel.Error,
        Message = "Audit log cleanup encountered an error" )]
    private static partial void LogCleanupError( ILogger logger, Exception ex );

    [LoggerMessage( Level = LogLevel.Warning,
        Message = "Retention cleanup summary audit failed (attempt {Attempt}), retrying" )]
    private static partial void LogSummaryAuditRetry( ILogger logger, int attempt, Exception ex );

    [LoggerMessage( Level = LogLevel.Error,
        Message = "Retention cleanup summary audit exhausted all retries. Deleted={Deleted}, Earliest={Earliest}, Latest={Latest}" )]
    private static partial void LogSummaryAuditExhausted( ILogger logger, int deleted, DateTime? earliest, DateTime? latest, Exception ex );
}
