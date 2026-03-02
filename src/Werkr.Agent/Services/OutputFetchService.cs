using Grpc.Core;
using Microsoft.Extensions.Options;

using Werkr.Common.Configuration;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Data.Entities.Registration;

namespace Werkr.Agent.Services;

/// <summary>
/// gRPC service hosted on the Agent that allows the Server to retrieve
/// full job output on demand. The Server stores only the <c>OutputPath</c>
/// after a job completes; this service reads the local log file and returns
/// its content when queried. All RPCs use <see cref="EncryptedEnvelope"/>.
/// </summary>
/// <param name="outputOptions">Job output directory configuration.</param>
/// <param name="logger">Logger instance.</param>
public sealed class OutputFetchService(
    IOptions<JobOutputOptions> outputOptions,
    ILogger<OutputFetchService> logger
) : OutputFetch.OutputFetchBase {

    private readonly string _outputDirectory = outputOptions.Value.OutputDirectory;

    /// <summary>
    /// Reads the full contents of a job's output log from the Agent's local disk.
    /// </summary>
    public override async Task<EncryptedEnvelope> GetJobOutput(
        EncryptedEnvelope request,
        ServerCallContext context ) {

        RegisteredConnection connection = GetConnection( context );
        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );

        GetJobOutputRequest inner = PayloadEncryptor.DecryptFromEnvelope<GetJobOutputRequest>(
            request, connection.SharedKey );

        if (string.IsNullOrWhiteSpace( inner.JobId )) {
            throw new RpcException( new Status( StatusCode.InvalidArgument, "Job ID is required." ) );
        }

        // Construct path from the output directory and job ID
        // Prefer job_id-based path, fall back to output_path if provided
        string filePath;
        if (!string.IsNullOrWhiteSpace( inner.OutputPath )) {
            // Sanitize: ensure the path stays within the output directory
            string fullPath = Path.GetFullPath( Path.Combine( _outputDirectory, inner.OutputPath ) );
            string normalizedBase = Path.GetFullPath( _outputDirectory );
            if (!fullPath.StartsWith( normalizedBase, StringComparison.OrdinalIgnoreCase )) {
                GetJobOutputResponse errorResponse = new( ) {
                    Found = false,
                    Error = "Output path is outside the configured output directory."
                };
                return PayloadEncryptor.EncryptToEnvelope( errorResponse, connection.SharedKey, keyId );
            }
            filePath = fullPath;
        } else {
            filePath = Path.Combine( _outputDirectory, $"{inner.JobId}.log" );
        }

        if (!File.Exists( filePath )) {
            if (logger.IsEnabled( LogLevel.Warning )) {
                logger.LogWarning( "Job output file not found: {FilePath} for Job {JobId}.", filePath, inner.JobId );
            }
            GetJobOutputResponse notFoundResponse = new( ) {
                Found = false,
                Error = $"Output file not found for job {inner.JobId}."
            };
            return PayloadEncryptor.EncryptToEnvelope( notFoundResponse, connection.SharedKey, keyId );
        }

        try {
            string content = await File.ReadAllTextAsync( filePath, context.CancellationToken );

            if (logger.IsEnabled( LogLevel.Debug )) {
                logger.LogDebug( "Served output for Job {JobId}, {Length} characters.", inner.JobId, content.Length );
            }

            GetJobOutputResponse response = new( ) {
                Found = true,
                Content = content,
            };
            return PayloadEncryptor.EncryptToEnvelope( response, connection.SharedKey, keyId );
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            logger.LogError( ex, "Failed to read output file {FilePath} for Job {JobId}.", filePath, inner.JobId );
            GetJobOutputResponse failResponse = new( ) {
                Found = false,
                Error = $"Failed to read output file: {ex.Message}",
            };
            return PayloadEncryptor.EncryptToEnvelope( failResponse, connection.SharedKey, keyId );
        }
    }

    private static RegisteredConnection GetConnection( ServerCallContext context ) {
        return context.UserState.TryGetValue( "Connection", out object? connObj ) && connObj is RegisteredConnection connection
            ? connection
            : throw new RpcException( new Status( StatusCode.Internal, "Connection not resolved by interceptor." ) );
    }
}
