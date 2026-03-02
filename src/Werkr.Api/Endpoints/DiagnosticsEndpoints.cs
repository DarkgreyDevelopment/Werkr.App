using Microsoft.EntityFrameworkCore;
using Werkr.Common.Auth;
using Werkr.Common.Models;
using Werkr.Data;

namespace Werkr.Api.Endpoints;

/// <summary>Maps the diagnostics health endpoint.</summary>
internal static class DiagnosticsEndpoints {
    /// <summary>Maps <c>GET /api/diagnostics/health</c>.</summary>
    public static WebApplication MapDiagnosticsEndpoints( this WebApplication app ) {
        _ = app.MapGet( "/api/diagnostics/health", async (
            WerkrDbContext appDbContext,
            CancellationToken ct ) => {
                List<DatabaseHealthDto> diagnostics = [];

                bool appConnected = await appDbContext.Database.CanConnectAsync( ct );
                List<string> appPending = [.. await appDbContext.Database.GetPendingMigrationsAsync( ct )];
                List<string> appApplied = [.. await appDbContext.Database.GetAppliedMigrationsAsync( ct )];

                diagnostics.Add( new DatabaseHealthDto(
                    "Application",
                    appDbContext.Database.ProviderName ?? "Unknown",
                    appConnected,
                    appApplied.Count,
                    appPending.Count,
                    appPending ) );

                return Results.Ok( diagnostics );
            } )
        .WithName( "GetDiagnosticsHealth" )
        .RequireAuthorization( Policies.CanRead );

        return app;
    }
}
