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
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly AuditLogOptions _options = options.Value;
    private readonly ILogger<AuditLogCleanupService> _logger = logger;

    /// <inheritdoc />
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        LogCleanupStarted( _logger, _options.RetentionDays );

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await CleanupAsync( stoppingToken );
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                LogCleanupError( _logger, ex );
            }

            // Wait 24 hours before next cleanup cycle
            await Task.Delay( TimeSpan.FromHours( 24 ), stoppingToken );
        }
    }

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

    [LoggerMessage( Level = LogLevel.Information,
        Message = "Audit log cleanup service started (retention: {RetentionDays} days)" )]
    private static partial void LogCleanupStarted( ILogger logger, int retentionDays );

    [LoggerMessage( Level = LogLevel.Information,
        Message = "Audit log cleanup deleted {Count} records older than {RetentionDays} days" )]
    private static partial void LogRecordsDeleted( ILogger logger, int count, int retentionDays );

    [LoggerMessage( Level = LogLevel.Error,
        Message = "Audit log cleanup encountered an error" )]
    private static partial void LogCleanupError( ILogger logger, Exception ex );
}
