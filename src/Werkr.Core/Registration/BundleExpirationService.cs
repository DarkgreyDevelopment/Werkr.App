using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Werkr.Common.Models;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Core.Registration;

/// <summary>
/// Background service that automatically transitions expired registration bundles
/// from <see cref="RegistrationStatus.Pending"/> to <see cref="RegistrationStatus.Expired"/>.
/// Runs every hour by default.
/// </summary>
/// <remarks>
/// Creates a new <see cref="BundleExpirationService"/>.
/// </remarks>
/// <param name="scopeFactory">Service scope factory for creating database contexts.</param>
/// <param name="logger">Logger for diagnostics.</param>
/// <param name="interval">How often to check for expired bundles (default: 1 hour).</param>
public class BundleExpirationService(
    IServiceScopeFactory scopeFactory,
    ILogger<BundleExpirationService> logger,
    TimeSpan? interval = null
) : BackgroundService {
    private readonly TimeSpan _interval = interval ?? TimeSpan.FromHours( 1 );

    /// <inheritdoc/>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "BundleExpirationService started. Checking every {Interval}.",
                _interval
            );
        }

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await ExpireStaleBundlesAsync( stoppingToken );
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                logger.LogError(
                    ex,
                    "Error in BundleExpirationService."
                );
            }

            await Task.Delay(
                _interval,
                stoppingToken
            );
        }
    }

    private async Task ExpireStaleBundlesAsync( CancellationToken ct ) {
        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext dbContext = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

        DateTime now = DateTime.UtcNow;

        List<RegistrationBundle> staleBundles = await dbContext.RegistrationBundles
            .Where( b => b.Status == RegistrationStatus.Pending && b.ExpiresAt < now )
            .ToListAsync( ct );

        if (staleBundles.Count == 0) {
            return;
        }

        foreach (RegistrationBundle bundle in staleBundles) {
            bundle.Status = RegistrationStatus.Expired;
        }

        _ = await dbContext.SaveChangesAsync( ct );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Expired {Count} stale registration bundle(s).",
                staleBundles.Count
            );
        }
    }
}
