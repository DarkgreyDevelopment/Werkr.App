namespace Werkr.Api.Endpoints;

/// <summary>Maps the root and status endpoints.</summary>
internal static class StatusEndpoints {
    /// <summary>Maps <c>GET /</c> and <c>GET /api/status</c>.</summary>
    public static WebApplication MapStatusEndpoints( this WebApplication app ) {
        _ = app.MapGet( "/", ( ) => "Werkr API Service is running." );

        _ = app.MapGet( "/api/v1/status", ( ) => {
            return Results.Ok( new { status = "ok" } );
        } )
        .WithName( "GetStatus" )
        .AllowAnonymous( );

        return app;
    }
}
