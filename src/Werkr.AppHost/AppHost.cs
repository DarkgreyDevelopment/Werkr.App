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

        // Agent
        IResourceBuilder<ProjectResource> agent = builder.AddProject<Projects.Werkr_Agent>( "agent" )
            .WithHttpHealthCheck( "/health" )
            .WaitFor( werkrDb );

        // API Service — owns the application database
        IResourceBuilder<ProjectResource> apiService = builder.AddProject<Projects.Werkr_Api>( "api" )
            .WithHttpHealthCheck( "/health" )
            .WithReference( werkrDb )
            .WaitFor( werkrDb );

        // Server (Blazor UI) — owns the identity database
        _ = builder.AddProject<Projects.Werkr_Server>( "server" )
            .WithExternalHttpEndpoints( )
            .WithHttpHealthCheck( "/health" )
            .WithReference( apiService )
            .WithReference( agent )
            .WithReference( werkrIdentityDb )
            .WaitFor( apiService )
            .WaitFor( agent )
            .WaitFor( werkrIdentityDb );

        builder.Build( ).Run( );
    }
}
