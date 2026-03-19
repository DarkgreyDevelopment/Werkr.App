using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Werkr.Data;

namespace Werkr.Api.Services;

/// <summary>
/// Background service that runs daily to delete <see cref="Werkr.Data.Entities.Schedule.ScheduleAuditLog"/>
/// records older than the configured retention period.
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
    /// Performs a single audit log cleanup pass by deleting all <see cref="Werkr.Data.Entities.Schedule.ScheduleAuditLog"/> records whose <c>CreatedUtc</c> is older than the configured retention period.
    /// </summary>
    private async Task CleanupAsync( CancellationToken ct ) {
        using IServiceScope scope = _scopeFactory.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

        DateTime cutoff = DateTime.UtcNow.AddDays( -_options.RetentionDays );

        int deleted = await db.ScheduleAuditLogs
            .Where( log => log.CreatedUtc < cutoff )
            .ExecuteDeleteAsync( ct );

        if (deleted > 0) {
            LogRecordsDeleted( _logger, deleted, _options.RetentionDays );
        }
    }

    [LoggerMessage( Level = LogLevel.Warning,
        Message = "Configured SweepIntervalMinutes ({Configured}) is below minimum. Clamped to {Effective} minutes." )]
    private static partial void LogSweepIntervalClamped( ILogger logger, int configured, int effective );

    [LoggerMessage( Level = LogLevel.Information,
        Message = "Audit log cleanup service started (retention: {RetentionDays} days, sweep interval: {SweepIntervalMinutes} minutes)" )]
    private static partial void LogCleanupStarted( ILogger logger, int retentionDays, int sweepIntervalMinutes );

    [LoggerMessage( Level = LogLevel.Information,
        Message = "Audit log cleanup deleted {Count} records older than {RetentionDays} days" )]
    private static partial void LogRecordsDeleted( ILogger logger, int count, int retentionDays );

    [LoggerMessage( Level = LogLevel.Error,
        Message = "Audit log cleanup encountered an error" )]
    private static partial void LogCleanupError( ILogger logger, Exception ex );
}
