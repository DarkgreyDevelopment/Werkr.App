namespace Werkr.AppHost;

/// <summary>Aspire AppHost orchestrator entry point.</summary>
public class Program {
    /// <summary>Main entry point.</summary>
    /// <param name="args">Command-line arguments.</param>
    public static void Main( string[] args ) {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder( args );

        // Infrastructure — two separate databases
        IResourceBuilder<PostgresServerResource> postgres = builder.AddPostgres( "postgres" )
            .WithDataVolume( );

        IResourceBuilder<PostgresDatabaseResource> werkrDb = postgres.AddDatabase( "werkrdb" );
        IResourceBuilder<PostgresDatabaseResource> werkrIdentityDb = postgres.AddDatabase( "werkridentitydb" );

        // API Service — owns the application database.
        // Pin ports so the agent's stored gRPC RemoteUrl remains valid across restarts.
        IResourceBuilder<ProjectResource> apiService = builder.AddProject<Projects.Werkr_Api>( "api" )
            .WithEndpoint( "https", e => { e.Port = 7349; e.IsProxied = false; } )
            .WithEndpoint( "http", e => { e.Port = 5560; e.IsProxied = false; } )
            .WithHttpHealthCheck( "/health" )
            .WithReference( werkrDb )
            .WaitFor( werkrDb );

        // Agent — pin ports so the registered RemoteUrl survives Aspire restarts.
        // IsProxied = false makes the app bind directly on the port (no DCP proxy)
        // so gRPC works without HTTP/2 proxy issues.
        // Werkr__ApiUrl overrides the stored gRPC URL so the agent always reaches the
        // correct API endpoint, even if the DB record is stale from a prior registration.
        _ = builder.AddProject<Projects.Werkr_Agent>( "agent" )
            .WithEndpoint( "https", e => { e.Port = 7100; e.IsProxied = false; } )
            .WithEndpoint( "http", e => { e.Port = 5100; e.IsProxied = false; } )
            .WithEnvironment( "Werkr__AgentUrl", "https://localhost:7100" )
            .WithEnvironment( "Werkr__ApiUrl", apiService.GetEndpoint( "https" ) )
            .WithHttpHealthCheck( "/health" )
            .WaitFor( werkrDb );

        // Server (Blazor UI) — owns the identity database
        _ = builder.AddProject<Projects.Werkr_Server>( "server" )
            .WithEndpoint( "https", e => { e.Port = 7041; e.IsProxied = false; } )
            .WithEndpoint( "http", e => { e.Port = 5005; e.IsProxied = false; } )
            .WithExternalHttpEndpoints( )
            .WithHttpHealthCheck( "/health" )
            .WithReference( apiService )
            .WithReference( werkrIdentityDb )
            .WaitFor( apiService )
            .WaitFor( werkrIdentityDb );

        builder.Build( ).Run( );
    }
}
