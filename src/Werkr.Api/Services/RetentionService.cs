using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Werkr.Common.Models;
using Werkr.Common.Models.Audit;
using Werkr.Core.Audit;
using Werkr.Core.Configuration;
using Werkr.Core.Retention;
using Werkr.Data;
using Werkr.Data.Entities.Configuration;

namespace Werkr.Api.Services;

/// <summary>
/// Background service that periodically sweeps aged records according to
/// <see cref="RetentionPolicy"/> rows in the database. Providers registered
/// in <see cref="RetentionPolicyRegistry"/> perform the actual deletion.
/// Each provider's deletion and its corresponding audit event are committed
/// in the same transaction so that a later failure cannot cause audit gaps.
/// </summary>
public sealed partial class RetentionService(
    IServiceScopeFactory scopeFactory,
    RetentionPolicyRegistry registry,
    ILogger<RetentionService> logger
) : BackgroundService {

    /// <summary>Hard minimum sweep interval to prevent runaway deletion cycles.</summary>
    private const int MinSweepIntervalMinutes = 15;

    /// <summary>Default batch size for batched deletes.</summary>
    private const int DefaultBatchSize = 5_000;

    /// <summary>Configuration key for the sweep interval.</summary>
    private const string SweepIntervalKey = "retention.sweepIntervalMinutes";

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly RetentionPolicyRegistry _registry = registry;
    private readonly ILogger<RetentionService> _logger = logger;

    /// <inheritdoc />
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        LogRetentionServiceStarted( _logger );

        while (!stoppingToken.IsCancellationRequested) {
            int intervalMinutes;
            try {
                intervalMinutes = await ReadSweepIntervalAsync( stoppingToken );
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                LogConfigReadError( _logger, ex );
                intervalMinutes = 1440; // default 24h on config read failure
            }

            int effectiveInterval = Math.Max( intervalMinutes, MinSweepIntervalMinutes );
            if (intervalMinutes < MinSweepIntervalMinutes) {
                LogSweepIntervalClamped( _logger, intervalMinutes, effectiveInterval );
            }

            await Task.Delay( TimeSpan.FromMinutes( effectiveInterval ), stoppingToken );

            try {
                _ = await SweepNowAsync( dryRun: false, stoppingToken );
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                LogSweepError( _logger, ex );
            }
        }
    }

    /// <summary>
    /// Runs a retention sweep immediately. When <paramref name="dryRun"/> is true,
    /// returns preview results without deleting any records.
    /// </summary>
    /// <param name="dryRun">If true, preview only; if false, delete aged records.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Results for each entity type that has a registered provider.</returns>
    public async Task<IReadOnlyList<RetentionSweepResult>> SweepNowAsync( bool dryRun, CancellationToken ct ) {
        using IServiceScope scope = _scopeFactory.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

        List<RetentionPolicy> policies = await db.RetentionPolicies
            .Where( p => p.IsEnabled )
            .AsNoTracking( )
            .ToListAsync( ct );

        List<RetentionSweepResult> results = [];

        foreach (RetentionPolicy policy in policies) {
            if (!_registry.HasProvider( policy.EntityType )) {
                LogProviderNotFound( _logger, policy.EntityType );
                continue;
            }

            if (dryRun) {
                IRetentionPolicyProvider provider = ResolveScopedProvider( scope, policy.EntityType );
                RetentionPreview preview = await provider.PreviewAgedRecordsAsync( policy.RetentionDays, ct );
                results.Add( new RetentionSweepResult(
                    preview.EntityType, preview.EligibleCount,
                    preview.OldestTimestamp, preview.NewestTimestamp ) );
            } else {
                RetentionSweepResult result = await SweepEntityAsync(
                    scope, policy, ct );
                results.Add( result );
            }
        }

        return results;
    }

    /// <summary>
    /// Sweeps a single entity type: resolves the scoped provider, deletes aged
    /// records, and emits an audit event — all within the same transaction.
    /// </summary>
    private async Task<RetentionSweepResult> SweepEntityAsync(
        IServiceScope scope, RetentionPolicy policy, CancellationToken ct
    ) {
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        IAuditService auditService = scope.ServiceProvider.GetRequiredService<IAuditService>( );
        IRetentionPolicyProvider scopedProvider = ResolveScopedProvider( scope, policy.EntityType );

        await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync( ct );

        RetentionSweepResult result = await scopedProvider.DeleteAgedRecordsAsync(
            policy.RetentionDays, DefaultBatchSize, ct );

        if (result.DeletedCount > 0) {
            await LogEntityRetentionAuditEventAsync(
                auditService, result, policy.RetentionDays, ct );
            LogRecordsDeleted( _logger, result.DeletedCount, policy.EntityType, policy.RetentionDays );
        }

        await tx.CommitAsync( ct );

        return result;
    }

    /// <summary>
    /// Resolves a scoped retention provider from the DI container so it gets
    /// a DbContext and AuditService that share the same scope.
    /// </summary>
    private static IRetentionPolicyProvider ResolveScopedProvider( IServiceScope scope, string entityType ) {
        IEnumerable<IRetentionPolicyProvider> providers =
            scope.ServiceProvider.GetServices<IRetentionPolicyProvider>( );
        return providers.First( p => p.EntityType == entityType );
    }

    private async Task<int> ReadSweepIntervalAsync( CancellationToken ct ) {
        using IServiceScope scope = _scopeFactory.CreateScope( );
        IConfigurationResolutionService configService =
            scope.ServiceProvider.GetRequiredService<IConfigurationResolutionService>( );

        ConfigurationEntryDto? entry = await configService.GetByKeyAsync( SweepIntervalKey, ct );
        if (entry is not null && int.TryParse( entry.Value, out int minutes )) {
            return minutes;
        }

        return 1440; // default 24 hours
    }

    /// <summary>
    /// Logs an audit event for a single entity type's retention deletion.
    /// Uses <see cref="AuditEventType.AuditRetentionCleanup"/> for audit log
    /// self-deletion (per spec §12) and <see cref="AuditEventType.RetentionSweepCompleted"/>
    /// for all other entity types.
    /// </summary>
    private static async Task LogEntityRetentionAuditEventAsync(
        IAuditService auditService, RetentionSweepResult result, int retentionDays, CancellationToken ct
    ) {
        AuditEventType eventType = result.EntityType == "audit_log"
            ? AuditEventType.AuditRetentionCleanup
            : AuditEventType.RetentionSweepCompleted;

        object details = new {
            entityType = result.EntityType,
            deletedCount = result.DeletedCount,
            oldestDeleted = result.OldestDeleted,
            newestDeleted = result.NewestDeleted,
            retentionDays,
            additionalDetails = result.AdditionalDetails,
        };

        await auditService.LogAsync( new AuditEntry(
            EventTypeId: eventType.ToEventId( ),
            ActorId: null,
            ActorType: "System",
            EntityType: result.EntityType,
            EntityId: null,
            ActionPerformed: "RetentionSweep",
            Details: details
        ), ct );
    }

    [LoggerMessage( Level = LogLevel.Information,
        Message = "Retention service started." )]
    private static partial void LogRetentionServiceStarted( ILogger logger );

    [LoggerMessage( Level = LogLevel.Warning,
        Message = "Configured retention sweep interval ({Configured} min) is below minimum. Clamped to {Effective} min." )]
    private static partial void LogSweepIntervalClamped( ILogger logger, int configured, int effective );

    [LoggerMessage( Level = LogLevel.Information,
        Message = "Retention sweep deleted {Count} records for entity type '{EntityType}' (retention: {RetentionDays} days)." )]
    private static partial void LogRecordsDeleted( ILogger logger, int count, string entityType, int retentionDays );

    [LoggerMessage( Level = LogLevel.Error,
        Message = "Retention sweep encountered an error." )]
    private static partial void LogSweepError( ILogger logger, Exception ex );

    [LoggerMessage( Level = LogLevel.Error,
        Message = "Failed to read retention sweep interval from configuration." )]
    private static partial void LogConfigReadError( ILogger logger, Exception ex );

    [LoggerMessage( Level = LogLevel.Warning,
        Message = "No retention provider registered for entity type '{EntityType}'." )]
    private static partial void LogProviderNotFound( ILogger logger, string entityType );
}
