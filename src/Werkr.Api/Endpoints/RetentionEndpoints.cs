using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Werkr.Api.Services;
using Werkr.Common.Auth;
using Werkr.Common.Models;
using Werkr.Common.Models.Audit;
using Werkr.Core.Audit;
using Werkr.Core.Retention;
using Werkr.Data;
using Werkr.Data.Entities.Configuration;

namespace Werkr.Api.Endpoints;

/// <summary>Maps data-retention REST endpoints for managing retention policies and triggering sweeps.</summary>
internal static class RetentionEndpoints {

    /// <summary>Maps retention policy CRUD and sweep trigger endpoints.</summary>
    public static WebApplication MapRetentionEndpoints( this WebApplication app ) {

        // ── Trigger sweep (manual or dry-run) ──
        _ = app.MapPost( "/api/v1/retention/sweep", async (
            bool? dryRun,
            RetentionService retentionService,
            CancellationToken ct
        ) => {
            bool isDryRun = dryRun ?? false;
            IReadOnlyList<RetentionSweepResult> results = await retentionService.SweepNowAsync( isDryRun, ct );
            return Results.Ok( results );
        } )
        .WithName( "TriggerRetentionSweep" )
        .RequireAuthorization( Policies.IsAdmin );

        // ── List all retention policies ──
        _ = app.MapGet( "/api/v1/retention/policies", async (
            WerkrDbContext db,
            CancellationToken ct
        ) => {
            List<RetentionPolicyDto> policies = await db.RetentionPolicies
                .AsNoTracking( )
                .OrderBy( p => p.EntityType )
                .Select( p => new RetentionPolicyDto(
                    p.EntityType,
                    p.RetentionDays,
                    p.IsEnabled,
                    p.ModifiedUtc,
                    p.ModifiedByUserId
                ) )
                .ToListAsync( ct );

            return Results.Ok( policies );
        } )
        .WithName( "ListRetentionPolicies" )
        .RequireAuthorization( Policies.IsAdmin );

        // ── Update a retention policy ──
        _ = app.MapPut( "/api/v1/retention/policies/{entityType}", async (
            string entityType,
            RetentionPolicyUpdateRequest request,
            HttpContext httpContext,
            WerkrDbContext db,
            IAuditService auditService,
            CancellationToken ct
        ) => {
            if (request.RetentionDays < 1) {
                return Results.BadRequest( new { message = "RetentionDays must be at least 1." } );
            }

            RetentionPolicy? policy = await db.RetentionPolicies
                .FirstOrDefaultAsync( p => p.EntityType == entityType, ct );

            if (policy is null) {
                return Results.NotFound( new { message = $"No retention policy found for entity type '{entityType}'." } );
            }

            string userId = httpContext.User.FindFirst( ClaimTypes.NameIdentifier )?.Value ?? "unknown";

            int previousDays = policy.RetentionDays;
            bool previousEnabled = policy.IsEnabled;

            policy.RetentionDays = request.RetentionDays;
            if (request.IsEnabled.HasValue) {
                policy.IsEnabled = request.IsEnabled.Value;
            }
            policy.ModifiedUtc = DateTime.UtcNow;
            policy.ModifiedByUserId = userId;
            policy.LastUpdated = DateTime.UtcNow;

            _ = await db.SaveChangesAsync( ct );

            // Audit the change
            await auditService.LogAsync( new AuditEntry(
                EventTypeId: AuditEventType.RetentionSweepCompleted.ToEventId( ),
                ActorId: userId,
                ActorType: "User",
                EntityType: "RetentionPolicy",
                EntityId: entityType,
                ActionPerformed: "Updated",
                Details: new {
                    previousRetentionDays = previousDays,
                    newRetentionDays = request.RetentionDays,
                    previousEnabled,
                    newEnabled = policy.IsEnabled
                }
            ), ct );

            RetentionPolicyDto dto = new(
                policy.EntityType,
                policy.RetentionDays,
                policy.IsEnabled,
                policy.ModifiedUtc,
                policy.ModifiedByUserId
            );

            return Results.Ok( dto );
        } )
        .WithName( "UpdateRetentionPolicy" )
        .RequireAuthorization( Policies.IsAdmin );

        return app;
    }
}
