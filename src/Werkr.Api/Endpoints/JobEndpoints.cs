using Grpc.Net.Client;
using Werkr.Api.Models;
using Werkr.Common.Auth;
using Werkr.Common.Models;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Core.Tasks;
using Werkr.Data;
using Werkr.Data.Entities.Registration;
using Werkr.Data.Entities.Tasks;

namespace Werkr.Api.Endpoints;

/// <summary>Maps all job-related REST endpoints.</summary>
internal static class JobEndpoints {
    /// <summary>Maps job history, list, detail, and output endpoints.</summary>
    public static WebApplication MapJobEndpoints( this WebApplication app ) {
        _ = app.MapGet(
            "/api/tasks/{taskId}/jobs",
            async (
                long taskId,
                int? limit,
                JobExecutionService jobExecutionService,
                CancellationToken ct
            ) => {
                int effectiveLimit = Math.Clamp( limit ?? 50, 1, 500 );
                IReadOnlyList<WerkrJob> jobs = await jobExecutionService.GetJobHistoryAsync( taskId, effectiveLimit, ct );
                List<JobListDto> dtos = [.. jobs.Select( TaskMapper.ToJobListDto )];
                return Results.Ok( dtos );
            } )
        .WithName( "GetJobHistory" )
        .RequireAuthorization( Policies.CanRead );

        _ = app.MapGet(
            "/api/jobs",
            async (
                bool? success,
                DateTime? since,
                DateTime? until,
                int? limit,
                JobExecutionService jobExecutionService,
                CancellationToken ct
            ) => {
                int effectiveLimit = Math.Clamp( limit ?? 50, 1, 500 );
                IReadOnlyList<WerkrJob> jobs = await jobExecutionService.GetRecentJobsAsync(
                    success, since, until, effectiveLimit, ct );
                List<JobListDto> dtos = [.. jobs.Select( TaskMapper.ToJobListDto )];
                return Results.Ok( dtos );
            } )
        .WithName( "GetJobs" )
        .RequireAuthorization( Policies.CanRead );

        _ = app.MapGet( "/api/jobs/{id}", async (
            Guid id,
            JobExecutionService jobExecutionService,
            CancellationToken ct
        ) => {
            WerkrJob? job = await jobExecutionService.GetJobAsync( id, ct );
            return job is null ? Results.NotFound( ) : Results.Ok( TaskMapper.ToJobDto( job ) );
        } )
        .WithName( "GetJob" )
        .RequireAuthorization( Policies.CanRead );

        _ = app.MapGet(
            "/api/jobs/{id}/output",
            async (
                Guid id,
                JobExecutionService jobExecutionService,
                WerkrDbContext dbContext,
                AgentConnectionManager connectionManager,
                CancellationToken ct
            ) => {
                // Try local file first (for API-local execution or cached output)
                string? output = await jobExecutionService.GetJobOutputAsync( id, ct );
                if (output is not null) {
                    return Results.Text( output, "text/plain" );
                }

                // Local file not found — fetch from Agent via OutputFetch gRPC
                WerkrJob? job = await jobExecutionService.GetJobAsync( id, ct );
                if (job is null) {
                    return Results.NotFound( new { message = "Job not found." } );
                }

                if (job.AgentConnectionId is null) {
                    return Results.NotFound( new { message = "No output file found for this job." } );
                }

                try {
                    (GrpcChannel channel, RegisteredConnection connection) =
                        await connectionManager.GetChannelAsync( job.AgentConnectionId.Value, ct );

                    string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );
                    GetJobOutputRequest grpcRequest = new( ) {
                        JobId = id.ToString( ),
                        OutputPath = job.OutputPath ?? string.Empty,
                    };

                    EncryptedEnvelope requestEnvelope = PayloadEncryptor.EncryptToEnvelope(
                        grpcRequest, connection.SharedKey, keyId );

                    OutputFetch.OutputFetchClient client = new( channel );
                    EncryptedEnvelope responseEnvelope = await client.GetJobOutputAsync(
                        requestEnvelope,
                        AgentConnectionManager.CreateCallOptions(
                            connection,
                            timeout: TimeSpan.FromSeconds( 30 ),
                            cancellationToken: ct
                        )
                    );

                    GetJobOutputResponse grpcResponse = PayloadEncryptor.DecryptFromEnvelope<GetJobOutputResponse>(
                        responseEnvelope, connection.SharedKey );

                    return grpcResponse.Found
                        ? Results.Text( grpcResponse.Content, "text/plain" )
                        : Results.NotFound( new { message = grpcResponse.Error } );
                } catch (InvalidOperationException) {
                    // Agent connection not found or revoked
                    return Results.NotFound( new { message = "Agent unavailable. Cannot retrieve job output." } );
                } catch (Grpc.Core.RpcException) {
                    return Results.StatusCode( 502 );
                }
            } )
        .WithName( "GetJobOutput" )
        .RequireAuthorization( Policies.CanRead );

        return app;
    }
}
